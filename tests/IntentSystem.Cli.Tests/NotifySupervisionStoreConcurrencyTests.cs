using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G768: supervision JSONL appends must be atomic per record even when a
/// concurrent writer does not take the directory coordination lock.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionStoreConcurrencyTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const int ConcurrentAppendsPerWriter = 500;
    private const string WriterModeEnvironmentVariable = "G768_WRITER_MODE";
    private const string WriterKindEnvironmentVariable = "G768_WRITER_KIND";
    private const string WriterPathEnvironmentVariable = "G768_WRITER_PATH";
    private const string WriterGateEnvironmentVariable = "G768_WRITER_GATE";
    private const string WriterCountEnvironmentVariable = "G768_WRITER_COUNT";
    private const string WriterTestFilter =
        "FullyQualifiedName~IntentSystem.Cli.Tests.NotifySupervisionStoreConcurrencyTests.ConcurrentWriterWorker_G768";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "intent-g768-concurrency-" + Guid.NewGuid().ToString("N"));

    public NotifySupervisionStoreConcurrencyTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConcurrentLockedAndUncooperativeWriters_PreserveEveryRecord_G768()
    {
        foreach (var kind in Enum.GetValues<SupervisionRecordKind>())
        {
            var result = RunConcurrentWriters(kind);
            var expectedCount = ConcurrentAppendsPerWriter * 2;
            var actualIds = result.Records
                .Where(record => record is not null)
                .Select(record => record!.Id)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(
                result.Records.Count == expectedCount
                    && result.UnreadableLineCount == 0
                    && actualIds.SetEquals(result.ExpectedIds),
                $"G768 measured {result.Records.Count} parseable records in {result.TotalNonBlankLineCount} "
                    + $"nonblank lines; expected {expectedCount}; loss={expectedCount - result.Records.Count}; "
                    + $"unreadable_lines={result.UnreadableLineCount}; missing="
                    + string.Join(",", result.ExpectedIds.Except(actualIds, StringComparer.Ordinal).OrderBy(id => id)));
            Assert.Equal(
                ConcurrentAppendsPerWriter,
                result.Records.Count(record => record is not null
                    && record.Id.Contains("-locked-", StringComparison.Ordinal)));
            Assert.Equal(
                ConcurrentAppendsPerWriter,
                result.Records.Count(record => record is not null
                    && record.Id.Contains("-unlocked-", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ConcurrentWriterWorker_G768()
    {
        var mode = Environment.GetEnvironmentVariable(WriterModeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        var kind = Enum.Parse<SupervisionRecordKind>(
            Environment.GetEnvironmentVariable(WriterKindEnvironmentVariable)!);
        var path = Environment.GetEnvironmentVariable(WriterPathEnvironmentVariable)!;
        var gate = Environment.GetEnvironmentVariable(WriterGateEnvironmentVariable)!;
        var count = int.Parse(
            Environment.GetEnvironmentVariable(WriterCountEnvironmentVariable)!,
            System.Globalization.CultureInfo.InvariantCulture);

        while (!File.Exists(gate))
        {
            Thread.Yield();
        }

        for (var index = 0; index < count; index++)
        {
            var id = $"{kind.ToString().ToLowerInvariant()}-{mode}-{index:D4}";
            var entry = CreateEntry(kind, id);
            if (string.Equals(mode, "locked", StringComparison.Ordinal))
            {
                var result = WriteWithProductStore(path, kind, entry);
                Assert.True(result.Applied, result.Error);
            }
            else
            {
                WriteWithoutDirectoryLock(path, entry);
            }
        }
    }

    private ConcurrentWriteResult RunConcurrentWriters(SupervisionRecordKind kind)
    {
        var scenarioRoot = Path.Combine(root, kind.ToString().ToLowerInvariant());
        var artifactRoot = Path.Combine(scenarioRoot, "artifacts");
        var path = kind switch
        {
            SupervisionRecordKind.Cycle => NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team),
            SupervisionRecordKind.Stall => NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team),
            SupervisionRecordKind.PromptAudit => NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var gate = Path.Combine(scenarioRoot, "start");
        var expectedIds = Enumerable.Range(0, ConcurrentAppendsPerWriter)
            .SelectMany(index => new[]
            {
                $"{kind.ToString().ToLowerInvariant()}-locked-{index:D4}",
                $"{kind.ToString().ToLowerInvariant()}-unlocked-{index:D4}",
            })
            .ToHashSet(StringComparer.Ordinal);
        using var locked = StartWriter("locked", kind, path, gate);
        using var unlocked = StartWriter("unlocked", kind, path, gate);
        File.WriteAllText(gate, "go\n");

        locked.WaitForExit();
        unlocked.WaitForExit();
        var lockedOutput = ReadProcessOutput(locked);
        var unlockedOutput = ReadProcessOutput(unlocked);
        Assert.True(
            locked.ExitCode == 0,
            $"locked writer exited {locked.ExitCode}: {lockedOutput}");
        Assert.True(
            unlocked.ExitCode == 0,
            $"unlocked writer exited {unlocked.ExitCode}: {unlockedOutput}");

        var lines = File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var records = lines
            .Select(ParseRecord)
            .ToArray();
        return new ConcurrentWriteResult(
            expectedIds,
            records,
            lines.Length,
            records.Count(record => record is null));
    }

    private Process StartWriter(
        string mode,
        SupervisionRecordKind kind,
        string path,
        string gate)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(FindTestProject());
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(FindConfiguration());
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(WriterTestFilter);
        startInfo.ArgumentList.Add("--logger");
        startInfo.ArgumentList.Add("console;verbosity=minimal");
        startInfo.Environment[WriterModeEnvironmentVariable] = mode;
        startInfo.Environment[WriterKindEnvironmentVariable] = kind.ToString();
        startInfo.Environment[WriterPathEnvironmentVariable] = path;
        startInfo.Environment[WriterGateEnvironmentVariable] = gate;
        startInfo.Environment[WriterCountEnvironmentVariable] =
            ConcurrentAppendsPerWriter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process;
    }

    private static NotifySupervisionEvent CreateEntry(
        SupervisionRecordKind kind,
        string id) => kind switch
        {
            SupervisionRecordKind.Cycle => new NotifySupervisionEvent
            {
                Kind = "cycle",
                Cycle = new NotifySupervisionCycle
                {
                    CycleId = id,
                    StartedAt = new DateTimeOffset(2026, 8, 31, 4, 0, 0, TimeSpan.Zero),
                    CompletedAt = new DateTimeOffset(2026, 8, 31, 4, 0, 1, TimeSpan.Zero),
                    IntervalSeconds = 300,
                },
            },
            SupervisionRecordKind.Stall => new NotifySupervisionEvent
            {
                Kind = "open",
                Stall = new NotifySupervisionStallRecord
                {
                    Key = id,
                    Kind = "g768-test-stall",
                    OwnerRole = "orchestration",
                    Source = "g768-concurrency-test",
                    Summary = "concurrent test stall",
                    SurfacedAt = new DateTimeOffset(2026, 8, 31, 4, 0, 1, TimeSpan.Zero),
                },
            },
            SupervisionRecordKind.PromptAudit => new NotifySupervisionEvent
            {
                Kind = "prompt-audit",
                PromptAudit = new NotifyPromptAudit
                {
                    PromptKey = id,
                    Seat = "implementation",
                    Pane = "g768:p1",
                    AgentKind = "fixture",
                    PromptClass = "g768-test",
                    Rule = "g768-concurrency-test",
                    Actor = "g768",
                    Timestamp = new DateTimeOffset(2026, 8, 31, 4, 0, 1, TimeSpan.Zero),
                    Outcome = "observed",
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static NotifySupervisionWriteResult WriteWithProductStore(
        string path,
        SupervisionRecordKind kind,
        NotifySupervisionEvent entry) => kind switch
        {
            SupervisionRecordKind.Cycle => NotifySupervisionStore.RecordCycle(path, entry.Cycle!, write: true),
            SupervisionRecordKind.Stall => NotifySupervisionStore.OpenStall(path, entry.Stall!, write: true),
            SupervisionRecordKind.PromptAudit => NotifySupervisionStore.RecordPromptAudit(path, entry.PromptAudit!, write: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static void WriteWithoutDirectoryLock(string path, NotifySupervisionEvent entry)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static ParsedRecord? ParseRecord(string line)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions);
            Assert.NotNull(entry);
            return entry!.Kind switch
            {
                "cycle" => new ParsedRecord(entry.Cycle!.CycleId),
                "open" => new ParsedRecord(entry.Stall!.Key),
                "prompt-audit" => new ParsedRecord(entry.PromptAudit!.PromptKey),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected event kind '{entry.Kind}'."),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadProcessOutput(Process process) =>
        process.StandardOutput.ReadToEnd() + Environment.NewLine + process.StandardError.ReadToEnd();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".git"))
                || Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the child repository root.");
    }

    private static string FindTestProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "IntentSystem.Cli.Tests.csproj");
            if (File.Exists(project))
            {
                return project;
            }
        }

        throw new InvalidOperationException("Could not locate IntentSystem.Cli.Tests.csproj.");
    }

    private static string FindConfiguration() =>
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";

    private enum SupervisionRecordKind
    {
        Cycle,
        Stall,
        PromptAudit,
    }

    private sealed record ParsedRecord(string Id);

    private sealed record ConcurrentWriteResult(
        IReadOnlySet<string> ExpectedIds,
        IReadOnlyList<ParsedRecord> Records);
}
