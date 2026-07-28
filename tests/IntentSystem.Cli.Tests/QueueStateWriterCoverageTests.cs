using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G548 round 2: a source/call-path fixture pinning the "every canonical
/// queue-state writer goes through the shared guard" claim, so it cannot
/// regress silently.
///
/// The round-1 implementation missed two writers precisely because the claim
/// was verified by hand: <c>MetadataUpdateCommand</c> persisted raw queue JSON
/// directly, and <c>IntentDriftService</c> — in a DIFFERENT assembly, which is
/// why the guard now lives in <c>IntentSystem.Supervisor</c> rather than in
/// the CLI — serialized the shared queue state itself when enqueuing a
/// corrective item. Either path could still reproduce the stale whole-file
/// overwrite this slice exists to prevent.
///
/// A new writer added anywhere in <c>src/</c> that bypasses the guard fails
/// this test with the file and line to fix.
/// </summary>
public sealed class QueueStateWriterCoverageTests
{
    /// <summary>The one file allowed to write queue-state directly — it IS the guard.</summary>
    private const string GuardFileName = "QueueStatePersistence.cs";

    [Fact]
    public void EveryQueueStateWriteInSource_GoesThroughTheSharedGuard_G548()
    {
        var offenders = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            if (string.Equals(Path.GetFileName(file), GuardFileName, StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!IsDirectQueueStateWrite(lines[index]))
                {
                    continue;
                }

                offenders.Add($"{RepoRelative(file)}:{index + 1}: {lines[index].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "these queue-state writes bypass QueueStatePersistence and can silently drop another domain's items "
            + "(G548). Route them through QueueStatePersistence.Persist / PersistRawJson:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void TheGuardItselfIsInASharedAssemblyBothWriterAssembliesCanReach_G548()
    {
        // The round-1 gap was structural, not accidental: with the guard in
        // IntentSystem.Cli, IntentSystem.Drift COULD NOT have used it. Pin the
        // location so it cannot drift back up a layer.
        var guardPath = Path.Combine(RepoRoot(), "src", "IntentSystem.Supervisor", GuardFileName);
        Assert.True(File.Exists(guardPath), $"expected the shared guard at {guardPath}");

        var source = File.ReadAllText(guardPath);
        Assert.Contains("namespace IntentSystem.Supervisor;", source, StringComparison.Ordinal);
        Assert.Contains("public static class QueueStatePersistence", source, StringComparison.Ordinal);

        // IntentSystem.Supervisor must stay dependency-free so every writer
        // assembly can reference it.
        var supervisorProject = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "IntentSystem.Supervisor", "IntentSystem.Supervisor.csproj"));
        Assert.DoesNotContain("ProjectReference", supervisorProject, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownQueueStateWriters_AllReferenceTheGuard_G548()
    {
        // Belt and braces for the two writers the round-1 review caught: a
        // rename or refactor that drops the guard call from either of them
        // fails here with a message naming the incident.
        foreach (var (relativePath, description) in new[]
        {
            ("src/IntentSystem.Cli/Commands/MetadataUpdateCommand.cs", "the bounded controlled metadata writer"),
            ("src/IntentSystem.Drift/IntentDriftService.cs", "the drift service's corrective enqueue"),
        })
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(
                source.Contains("QueueStatePersistence.", StringComparison.Ordinal),
                $"{relativePath} ({description}) must persist queue-state through QueueStatePersistence — it was an "
                + "unguarded loss path in G548 round 1 and can reproduce the 2ab082cf stale whole-file overwrite.");
        }
    }

    /// <summary>
    /// A direct write is either an explicit queue-state serialization, or a
    /// <c>File.WriteAllText</c> whose target argument names a queue-state
    /// path. Both forms appeared in the round-1 gap.
    /// </summary>
    private static bool IsDirectQueueStateWrite(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
        {
            return false;
        }

        if (!trimmed.Contains("File.WriteAllText(", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Contains("QueueStateSerializer.Serialize", StringComparison.Ordinal))
        {
            return true;
        }

        // e.g. File.WriteAllText(queueStatePath, ...), (scopedQueueStatePath, ...), (legacyQueueStatePath, ...)
        return Regex.IsMatch(trimmed, @"File\.WriteAllText\(\s*[A-Za-z_][A-Za-z0-9_]*[Qq]ueue[A-Za-z0-9_]*", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static IEnumerable<string> EnumerateSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepoRelative(string path) =>
        Path.GetRelativePath(RepoRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IntentSystem.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
