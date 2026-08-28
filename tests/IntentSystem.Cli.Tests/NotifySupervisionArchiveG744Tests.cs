using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionArchiveG744Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g744-").FullName;
    private readonly DateTimeOffset now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    public NotifySupervisionArchiveG744Tests()
    {
        NotifyCommand.UtcNowFactory = () => now;
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifySupervisionStore.ArchiveFaultInjector = null;
        NotifySupervisionStore.WriteOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArchiveCommand_MovesPastWindowToPeriodFilesAndPrintsBeforeAfterListing()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var oldJuly = Cycle("old-july", new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));
        var oldAugust = Cycle("old-august", new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var recent = Cycle("recent", new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        WriteCycle(cyclePath, oldJuly);
        WriteCycle(cyclePath, oldAugust);
        WriteCycle(cyclePath, recent);

        var beforeListing = ListStateFiles(artifactRoot);
        using var output = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            context,
            [
                "archive",
                "--domain", Domain,
                "--team", Team,
                "--live-window-days", "7",
                "--write",
                "--format", "json",
            ],
            output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement;
        Assert.Equal("supervise-archive", result.GetProperty("operation").GetString());
        Assert.Equal(7, result.GetProperty("live_window_days").GetInt32());
        Assert.Equal(2, result.GetProperty("records_moved").GetInt32());
        Assert.Equal(1, result.GetProperty("records_retained").GetInt32());
        Assert.Equal(0, result.GetProperty("records_discarded").GetInt32());
        Assert.True(result.GetProperty("live_safe").GetBoolean());
        Assert.Contains(
            result.GetProperty("archive_files").EnumerateArray(),
            item => item.GetProperty("period").GetString() == "2026-07");
        Assert.Contains(
            result.GetProperty("archive_files").EnumerateArray(),
            item => item.GetProperty("period").GetString() == "2026-08");

        var archiveDirectory = NotifySupervisionStore.ResolveCycleArchiveDirectoryPath(
            artifactRoot,
            Domain,
            Team);
        var afterListing = ListStateFiles(artifactRoot);
        Assert.Equal(
            [".supervision.lock", "cycles-archive/2026-07.jsonl", "cycles-archive/2026-08.jsonl", "cycles.jsonl"],
            afterListing);
        Assert.DoesNotContain("cycles-archive", beforeListing);
        Assert.Contains("cycles.jsonl", beforeListing);
        Assert.Equal(
            ["2026-07.jsonl", "2026-08.jsonl"],
            Directory.EnumerateFiles(archiveDirectory, "*.jsonl")
                .Select(path => Path.GetFileName(path)!)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray());
        Assert.Contains("recent", File.ReadAllText(cyclePath), StringComparison.Ordinal);
        Assert.DoesNotContain("old-july", File.ReadAllText(cyclePath), StringComparison.Ordinal);
        Assert.Contains(
            "old-july",
            File.ReadAllText(
                NotifySupervisionStore.ResolveCycleArchivePath(
                    artifactRoot,
                    Domain,
                    Team,
                    oldJuly.CompletedAt)),
            StringComparison.Ordinal);
        var archiveBytes = Directory.EnumerateFiles(archiveDirectory, "*.jsonl")
            .Select(path => new FileInfo(path).Length)
            .Sum();
        Assert.True(new FileInfo(cyclePath).Length < archiveBytes);

        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(state.Resolved, state.Error);
        Assert.Equal(3, state.CycleHistory.Count);
        Assert.Equal("recent", state.LastCycle?.CycleId);
        Console.WriteLine($"G744 archive listing before: {string.Join(", ", beforeListing)}");
        Console.WriteLine($"G744 archive listing after: {string.Join(", ", afterListing)}");
        Console.WriteLine($"G744 archive result: {output}");
    }

    [Fact]
    public void Reader_CombinesArchiveAndLiveHistoryWithoutDiscarding()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var old = Cycle("reader-old", now.AddDays(-30));
        var current = Cycle("reader-current", now.AddHours(-1));
        WriteCycle(cyclePath, old);
        WriteCycle(cyclePath, current);

        var result = NotifySupervisionStore.Archive(
            artifactRoot,
            Domain,
            Team,
            write: true,
            occurredAt: now,
            liveWindowDays: 7);

        Assert.True(result.Applied, result.Error);
        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(state.Resolved, state.Error);
        Assert.Equal(
            ["reader-old", "reader-current"],
            state.CycleHistory.Select(cycle => cycle.CycleId));
        Assert.Equal("reader-current", state.LastCycle?.CycleId);
        Assert.Equal(0, result.RecordsDiscarded);
        Assert.Equal(2, result.RecordsMoved + result.RecordsRetained);
    }

    [Fact]
    public async Task ConcurrentAppend_WaitsAtArchiveBoundaryAndIsRecordedExactlyOnce()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var old = Cycle("concurrent-old", now.AddDays(-30));
        WriteCycle(cyclePath, old);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        NotifySupervisionStore.ArchiveFaultInjector = point =>
        {
            if (point == NotifySupervisionArchiveFaultPoint.BeforeReplacement)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("archive test release was not signalled");
                }
            }
        };

        var archiveTask = Task.Run(() => NotifySupervisionStore.Archive(
            artifactRoot,
            Domain,
            Team,
            write: true,
            occurredAt: now,
            liveWindowDays: 7));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        var concurrent = Cycle("concurrent-during-move", now.AddMinutes(-1));
        var appendTask = Task.Run(() => NotifySupervisionStore.RecordCycle(
            cyclePath,
            concurrent,
            write: true));
        try
        {
            await Task.Delay(100);
            Assert.False(
                appendTask.IsCompleted,
                "the append completed before the archive released the shared directory boundary");
        }
        finally
        {
            release.Set();
        }

        var archiveResult = await archiveTask;
        var appendResult = await appendTask;
        Assert.True(archiveResult.Applied, archiveResult.Error);
        Assert.True(appendResult.Applied, appendResult.Error);

        var archivePath = NotifySupervisionStore.ResolveCycleArchivePath(
            artifactRoot,
            Domain,
            Team,
            old.CompletedAt);
        Assert.DoesNotContain("concurrent-during-move", File.ReadAllText(archivePath), StringComparison.Ordinal);
        Assert.Equal(
            1,
            File.ReadLines(cyclePath).Count(line =>
                line.Contains("concurrent-during-move", StringComparison.Ordinal)));

        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(state.Resolved, state.Error);
        Assert.Equal(
            1,
            state.CycleHistory.Count(cycle => cycle.CycleId == "concurrent-during-move"));
        Assert.Equal(2, state.CycleHistory.Count);
        Console.WriteLine("G744 concurrent archive: append waited for shared lock; concurrent record appeared once in live and once in reader history.");
    }

    [Fact]
    public void AlreadyInsideWindow_FixtureDirectoryRemainsByteIdentical()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var directory = NotifySupervisionStore.ResolveDirectory(artifactRoot, Domain, Team);
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "tests",
            "IntentSystem.Cli.Tests",
            "Fixtures",
            "g744-supervision-state",
            Domain,
            Team,
            NotifySupervisionStore.CycleFileName);
        var livePath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        File.Copy(fixture, livePath);
        File.WriteAllBytes(Path.Combine(directory, ".supervision.lock"), []);
        var before = SnapshotDirectory(directory);

        var result = NotifySupervisionStore.Archive(
            artifactRoot,
            Domain,
            Team,
            write: true,
            occurredAt: now,
            liveWindowDays: 7);

        Assert.False(result.WouldChange);
        Assert.Equal(0, result.RecordsMoved);
        var after = SnapshotDirectory(directory);
        Assert.Equal(
            before.Keys.OrderBy(item => item, StringComparer.Ordinal),
            after.Keys.OrderBy(item => item, StringComparer.Ordinal));
        foreach (var (relativePath, bytes) in before)
        {
            Assert.Equal(bytes, after[relativePath]);
        }
        Assert.False(Directory.Exists(
            NotifySupervisionStore.ResolveCycleArchiveDirectoryPath(artifactRoot, Domain, Team)));
        Console.WriteLine($"G744 inside-window fixture: byte-identical files={string.Join(", ", before.Keys.OrderBy(item => item, StringComparer.Ordinal))}");
    }

    private void WriteCycle(string path, NotifySupervisionCycle cycle)
    {
        var result = NotifySupervisionStore.RecordCycle(path, cycle, write: true);
        Assert.True(result.Applied, result.Error);
    }

    private static NotifySupervisionCycle Cycle(string id, DateTimeOffset completedAt) => new()
    {
        CycleId = id,
        StartedAt = completedAt.AddMinutes(-1),
        CompletedAt = completedAt,
        IntervalSeconds = 300,
    };

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private static string[] ListStateFiles(string artifactRoot)
    {
        var directory = NotifySupervisionStore.ResolveDirectory(artifactRoot, Domain, Team);
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, byte[]> SnapshotDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(directory, path),
                File.ReadAllBytes,
                StringComparer.Ordinal);
}
