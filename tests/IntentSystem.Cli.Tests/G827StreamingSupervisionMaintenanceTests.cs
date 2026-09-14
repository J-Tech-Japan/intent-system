using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G827: shrink and archive stream supervision files. These tests pin the
/// streamed output byte-for-byte against the string semantics it replaced
/// (kept here as a private oracle), for every newline, byte-order-mark,
/// blank-line and trailing-newline edge the string code handled.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G827StreamingSupervisionMaintenanceTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string root = Directory.CreateTempSubdirectory("notify-g827-").FullName;

    public void Dispose()
    {
        NotifySupervisionStore.ArchiveFaultInjector = null;
        NotifySupervisionStore.ShrinkFaultInjector = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static TheoryData<string> LiveShapes() =>
    [
        "lf",
        "crlf",
        "mixed",
        "no-trailing-newline",
        "bom",
        "blank-and-unparseable",
        "lone-carriage-return",
        "only-old",
        "empty",
    ];

    [Theory]
    [MemberData(nameof(LiveShapes))]
    public void Archive_StreamedBytesEqualTheStringSemantics_ForEveryEdgeShape(string shape)
    {
        var liveBytes = BuildLive(shape);
        var existingArchive = shape is "only-old" or "mixed"
            ? Utf8NoBom.GetBytes(Cycle("pre-existing", new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero)))
            : null; // an existing archive WITHOUT a trailing newline
        var directory = TeamDirectory();
        Directory.CreateDirectory(Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName));
        File.WriteAllBytes(Path.Combine(directory, NotifySupervisionStore.CycleFileName), liveBytes);
        var julyPath = Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName, "2026-07.jsonl");
        if (existingArchive is not null)
        {
            File.WriteAllBytes(julyPath, existingArchive);
        }

        var cutoff = Now.AddDays(-7);
        var expected = OracleArchive(liveBytes, existingArchive, cutoff);

        var dryRun = NotifySupervisionStore.Archive(ArtifactRoot(), Domain, Team, write: false, occurredAt: Now, liveWindowDays: 7);
        Assert.Null(dryRun.Error);
        Assert.Equal(expected.WouldChange, dryRun.WouldChange);
        Assert.Equal(expected.RecordsMoved, dryRun.RecordsMoved);
        Assert.Equal(expected.RecordsRetained, dryRun.RecordsRetained);
        Assert.Equal(expected.BeforeRecords, dryRun.BeforeLiveRecordCount);
        if (expected.WouldChange)
        {
            Assert.Equal(expected.Live.LongLength, dryRun.AfterLiveBytes);
            Assert.Equal(
                expected.Archives.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (item.Key, (long)item.Value.Length)),
                dryRun.Archives.Select(item => (item.Period, item.AfterBytes)));
        }

        // Dry-run changed nothing.
        Assert.Equal(liveBytes, File.ReadAllBytes(Path.Combine(directory, NotifySupervisionStore.CycleFileName)));

        var write = NotifySupervisionStore.Archive(ArtifactRoot(), Domain, Team, write: true, occurredAt: Now, liveWindowDays: 7);
        Assert.Null(write.Error);
        Assert.Equal(dryRun.AfterLiveBytes, write.AfterLiveBytes);
        Assert.Equal(dryRun.RecordsMoved, write.RecordsMoved);
        var livePath = Path.Combine(directory, NotifySupervisionStore.CycleFileName);
        Assert.Equal(expected.WouldChange ? expected.Live : liveBytes, File.ReadAllBytes(livePath));
        foreach (var (period, bytes) in expected.Archives)
        {
            Assert.Equal(
                bytes,
                File.ReadAllBytes(Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName, period + ".jsonl")));
        }

        Assert.False(File.Exists(Path.Combine(directory, NotifySupervisionStore.ArchiveTransactionFileName)));
        Assert.Empty(Directory.EnumerateDirectories(directory, ".archive-transaction-*"));
    }

    public static TheoryData<string> CycleFileShapes() =>
    [
        "lf",
        "crlf",
        "mixed",
        "no-trailing-newline",
        "bom",
        "blank-and-unparseable",
        "lone-carriage-return",
        "empty",
    ];

    [Theory]
    [MemberData(nameof(CycleFileShapes))]
    public void Shrink_StreamedCycleRewriteEqualsTheStringSemantics(string shape)
    {
        var cycleBytes = BuildLive(shape);
        var directory = TeamDirectory();
        Directory.CreateDirectory(directory);
        var cyclePath = Path.Combine(directory, NotifySupervisionStore.CycleFileName);
        File.WriteAllBytes(cyclePath, cycleBytes);
        var expected = OracleShrinkCopy(cycleBytes);

        var dryRun = NotifySupervisionStore.Shrink(ArtifactRoot(), Domain, Team, write: false, Now, "stopped", null);
        Assert.Null(dryRun.Error);
        Assert.Equal(expected.LongLength, dryRun.CycleFile.AfterBytes);
        Assert.Equal(cycleBytes, File.ReadAllBytes(cyclePath));

        var write = NotifySupervisionStore.Shrink(ArtifactRoot(), Domain, Team, write: true, Now, "stopped", null);
        Assert.Null(write.Error);
        Assert.Equal(expected, File.ReadAllBytes(cyclePath));
        Assert.False(File.Exists(Path.Combine(directory, NotifySupervisionStore.ShrinkTransactionFileName)));
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    public void Shrink_CompactsLegacyStallsWithTheSameJoinSemantics(string newline, bool trailingNewline)
    {
        var directory = TeamDirectory();
        Directory.CreateDirectory(directory);
        var stallsPath = Path.Combine(directory, NotifySupervisionStore.StallFileName);

        // The per-line compaction is independent of the file's newline shape:
        // measure each line's compacted form alone, then pin the join.
        var legacy = Enumerable.Range(0, 3).Select(LegacyStall).ToArray();
        var compacted = legacy.Select(CompactAlone).ToArray();

        var content = string.Join(newline, legacy) + (trailingNewline ? newline : string.Empty);
        File.WriteAllBytes(stallsPath, Utf8NoBom.GetBytes(content));
        var expected = Utf8NoBom.GetBytes(string.Join(newline, compacted) + (trailingNewline ? newline : string.Empty));

        var write = NotifySupervisionStore.Shrink(ArtifactRoot(), Domain, Team, write: true, Now, "stopped", null);

        Assert.Null(write.Error);
        Assert.Equal(expected, File.ReadAllBytes(stallsPath));
        Assert.Equal(3, write.StallFile.AfterRecords);
    }

    [Fact]
    public void ArchiveRecovery_StreamsAStagedReplacementAndRefusesACorruptStage()
    {
        var directory = TeamDirectory();
        Directory.CreateDirectory(directory);
        var livePath = Path.Combine(directory, NotifySupervisionStore.CycleFileName);
        File.WriteAllBytes(livePath, BuildLive("lf"));
        NotifySupervisionStore.ArchiveFaultInjector = point =>
        {
            if (point == NotifySupervisionArchiveFaultPoint.BeforeReplacement)
            {
                throw new IOException("simulated crash before replacement");
            }
        };

        var crashed = NotifySupervisionStore.Archive(ArtifactRoot(), Domain, Team, write: true, occurredAt: Now, liveWindowDays: 7);
        Assert.NotNull(crashed.Error);
        NotifySupervisionStore.ArchiveFaultInjector = null;

        var stage = Assert.Single(Directory.EnumerateDirectories(directory, ".archive-transaction-*"));
        var stagedLive = Path.Combine(stage, "live.jsonl");
        var original = File.ReadAllBytes(stagedLive);

        // Corrupt the staged live file: recovery must refuse, not publish it.
        File.WriteAllBytes(stagedLive, [.. original, (byte)'x']);
        var refused = NotifySupervisionStore.Archive(ArtifactRoot(), Domain, Team, write: true, occurredAt: Now, liveWindowDays: 7);
        Assert.Contains("corrupt", refused.Error, StringComparison.Ordinal);

        // Restore the stage: recovery completes and the result equals a clean run.
        File.WriteAllBytes(stagedLive, original);
        var recovered = NotifySupervisionStore.Archive(ArtifactRoot(), Domain, Team, write: true, occurredAt: Now, liveWindowDays: 7);
        Assert.Null(recovered.Error);
        Assert.Equal(OracleArchive(BuildLive("lf"), null, Now.AddDays(-7)).Live, File.ReadAllBytes(livePath));
        Assert.False(File.Exists(Path.Combine(directory, NotifySupervisionStore.ArchiveTransactionFileName)));
    }

    [Fact]
    public void ArchiveAndShrinkSource_NoLongerLoadWholeSupervisionFiles()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "src",
            "IntentSystem.Cli",
            "Commands",
            "NotifySupervisionStore.cs"));
        foreach (var method in new[]
        {
            "PlanCycleArchive",
            "ExecuteCycleArchiveTransaction",
            "RecoverPendingArchiveTransaction",
            "PlanFile",
            "ExecuteShrinkTransaction",
            "RecoverPendingShrinkTransaction",
        })
        {
            var body = MethodBody(source, method);
            Assert.DoesNotContain("File.ReadAllBytes(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllLines(", body, StringComparison.Ordinal);

            // Only the small JSON transaction journal may still be read whole.
            var wholeTextReads = body.Split("File.ReadAllText(").Skip(1).ToArray();
            Assert.All(wholeTextReads, read => Assert.StartsWith("transactionPath", read.TrimStart(), StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EnsureIgnore_AddsTheStallRuleOnceAndKeepsAHandAddedRule()
    {
        var supervisionRoot = ArtifactRoot();
        Directory.CreateDirectory(supervisionRoot);
        var ignorePath = NotifySupervisionStore.ResolveCycleHistoryIgnorePath(supervisionRoot);
        File.WriteAllText(ignorePath, "**/cycles.jsonl\n**/cycles-archive/\n# hand-added\n**/stalls.jsonl\n");

        var result = NotifySupervisionStore.EnsureCycleHistoryIgnore(supervisionRoot, write: true);

        Assert.Null(result.Error);
        Assert.False(result.WouldChange);
        Assert.Equal(1, File.ReadAllLines(ignorePath).Count(line => line.Trim() == "**/stalls.jsonl"));

        File.WriteAllText(ignorePath, "**/cycles.jsonl\n**/cycles-archive/\n");
        var added = NotifySupervisionStore.EnsureCycleHistoryIgnore(supervisionRoot, write: true);
        Assert.True(added.Applied);
        Assert.Equal(["**/cycles.jsonl", "**/cycles-archive/", "**/stalls.jsonl"], File.ReadAllLines(ignorePath));
    }

    [Fact]
    public void ReadWithoutCycleHistory_FindsTheSameLastCyclesAsTheFullRead()
    {
        var directory = TeamDirectory();
        Directory.CreateDirectory(Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName));
        var tie = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        string CycleWith(string id, DateTimeOffset completedAt, string trigger) =>
            JsonSerializer.Serialize(
                new NotifySupervisionEvent
                {
                    Kind = "cycle",
                    Cycle = new NotifySupervisionCycle
                    {
                        CycleId = id,
                        StartedAt = completedAt.AddMinutes(-1),
                        CompletedAt = completedAt,
                        IntervalSeconds = 300,
                        Trigger = trigger,
                    },
                },
                JsonOptions);

        // Archive holds the latest interval cycle; the live file holds a later
        // event cycle, a tie on the interval timestamp read later, a malformed
        // line, and an older cycle written out of order.
        File.WriteAllText(
            Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName, "2026-08.jsonl"),
            CycleWith("archived-interval", tie, "interval") + "\n" + CycleWith("archived-old", tie.AddDays(-3), string.Empty) + "\n");
        File.WriteAllText(
            Path.Combine(directory, NotifySupervisionStore.CycleFileName),
            CycleWith("live-tie-interval", tie, string.Empty) + "\n"
            + "{not json\n"
            + CycleWith("live-event", tie.AddHours(2), "event") + "\n"
            + CycleWith("live-out-of-order", tie.AddDays(-10), "interval") + "\n");

        var full = NotifySupervisionStore.Read(ArtifactRoot(), Domain, Team);
        var streamed = NotifySupervisionStore.Read(ArtifactRoot(), Domain, Team, includeCycleHistory: false);

        Assert.True(full.Resolved, full.Error);
        Assert.Equal(full.Resolved, streamed.Resolved);
        Assert.Equal("live-event", full.LastCycle?.CycleId);
        Assert.Equal(full.LastCycle?.CycleId, streamed.LastCycle?.CycleId);
        Assert.Equal("live-tie-interval", full.LastIntervalCycle?.CycleId);
        Assert.Equal(full.LastIntervalCycle?.CycleId, streamed.LastIntervalCycle?.CycleId);
        Assert.Empty(streamed.CycleHistory);
        Assert.NotEmpty(full.CycleHistory);
    }

    // ── fixtures ──────────────────────────────────────────────────────

    private string ArtifactRoot() => Path.Combine(root, ".intent-cli", "supervision");

    private string TeamDirectory() => NotifySupervisionStore.ResolveDirectory(ArtifactRoot(), Domain, Team);

    private static string Cycle(string id, DateTimeOffset completedAt) =>
        JsonSerializer.Serialize(
            new NotifySupervisionEvent
            {
                Kind = "cycle",
                Cycle = new NotifySupervisionCycle
                {
                    CycleId = id,
                    StartedAt = completedAt.AddMinutes(-1),
                    CompletedAt = completedAt,
                    IntervalSeconds = 300,
                },
            },
            JsonOptions);

    private static byte[] BuildLive(string shape)
    {
        var julyOld = Cycle("july-old", new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero));
        var augustOld = Cycle("august-old-é", new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        var recent = Cycle("recent", Now.AddHours(-1));
        return shape switch
        {
            "lf" => Utf8NoBom.GetBytes($"{julyOld}\n{augustOld}\n{recent}\n"),
            "crlf" => Utf8NoBom.GetBytes($"{julyOld}\r\n{augustOld}\r\n{recent}\r\n"),
            "mixed" => Utf8NoBom.GetBytes($"{julyOld}\n{recent}\r\n{augustOld}\n{recent}\n"),
            "no-trailing-newline" => Utf8NoBom.GetBytes($"{julyOld}\n{recent}"),
            "bom" => [.. Bom, .. Utf8NoBom.GetBytes($"{recent}\n{julyOld}\n")],
            "blank-and-unparseable" => Utf8NoBom.GetBytes($"{julyOld}\n\n   \n{{not json\n{recent}\n"),
            "lone-carriage-return" => Utf8NoBom.GetBytes($"{julyOld}\n{recent}\rtrailing\n"),
            "only-old" => Utf8NoBom.GetBytes($"{julyOld}\n{augustOld}\n"),
            "empty" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private static string LegacyStall(int index)
    {
        var record = new NotifySupervisionStallRecord
        {
            Key = $"legacy:{index}",
            Kind = "g827-legacy-stall",
            OwnerRole = "orchestration",
            SubjectRole = "implementation",
            Source = "g827-test",
            Summary = "A retained legacy stall.",
            SurfacedAt = Now.AddSeconds(index),
            RegistrationDefinition = NotifySupervisionStore.HerdrRegistrationDefinition,
            RegistrationLookup = $"legacy lookup {index}",
            RegistrationResult = "registration-missing; foreground-processes-absent",
            Evidence = [$"registration_definition:{NotifySupervisionStore.HerdrRegistrationDefinition}"],
        };
        return JsonSerializer.Serialize(new NotifySupervisionEvent { Kind = "open", Stall = record }, JsonOptions);
    }

    private string CompactAlone(string line)
    {
        var scratch = Directory.CreateTempSubdirectory("notify-g827-one-").FullName;
        try
        {
            var artifactRoot = Path.Combine(scratch, ".intent-cli", "supervision");
            var directory = NotifySupervisionStore.ResolveDirectory(artifactRoot, Domain, Team);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, NotifySupervisionStore.StallFileName);
            File.WriteAllText(path, line + "\n", Utf8NoBom);
            var result = NotifySupervisionStore.Shrink(artifactRoot, Domain, Team, write: true, Now, "stopped", null);
            Assert.Null(result.Error);
            return File.ReadAllText(path, Utf8NoBom).TrimEnd('\n');
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string MethodBody(string source, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(source, $@"\n    private static [^\n(]* {name}\(");
        Assert.True(match.Success, $"method {name} not found");
        var start = match.Index;
        var open = source.IndexOf("\n    {", start, StringComparison.Ordinal);
        var close = source.IndexOf("\n    }\n", open, StringComparison.Ordinal);
        return source[open..close];
    }

    // ── oracle: the string implementation G827 replaced ─────────────────

    private sealed record ArchiveOracle(
        bool WouldChange,
        int BeforeRecords,
        int RecordsMoved,
        int RecordsRetained,
        byte[] Live,
        IReadOnlyDictionary<string, byte[]> Archives);

    private static ArchiveOracle OracleArchive(byte[] liveBytes, byte[]? existingJuly, DateTimeOffset cutoff)
    {
        var original = Decode(liveBytes);
        var lines = Split(original, out var newline, out var trailingNewline);
        var retained = new List<string>();
        var grouped = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var moved = 0;
        foreach (var line in lines)
        {
            if (TryTimestamp(line, out var timestamp) && timestamp < cutoff)
            {
                var period = timestamp.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
                if (!grouped.TryGetValue(period, out var list))
                {
                    grouped[period] = list = [];
                }

                list.Add(line);
                moved++;
            }
            else
            {
                retained.Add(line);
            }
        }

        var live = retained.Count == 0
            ? string.Empty
            : string.Join(newline, retained) + (trailingNewline ? newline : string.Empty);
        var archives = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (period, additions) in grouped)
        {
            var existing = period == "2026-07" && existingJuly is not null ? Decode(existingJuly) : string.Empty;
            var builder = new StringBuilder(existing);
            if (builder.Length > 0 && !existing.EndsWith(newline, StringComparison.Ordinal))
            {
                builder.Append(newline);
            }

            builder.Append(string.Join(newline, additions));
            builder.Append(newline);
            archives[period] = Utf8NoBom.GetBytes(builder.ToString());
        }

        return new ArchiveOracle(
            moved > 0,
            lines.Count(line => !string.IsNullOrWhiteSpace(line)),
            moved,
            moved > 0
                ? retained.Count(line => !string.IsNullOrWhiteSpace(line))
                : lines.Count(line => !string.IsNullOrWhiteSpace(line)),
            Utf8NoBom.GetBytes(live),
            archives);
    }

    private static byte[] OracleShrinkCopy(byte[] bytes)
    {
        var original = Decode(bytes);
        var lines = Split(original, out var newline, out var trailingNewline);
        return Utf8NoBom.GetBytes(string.Join(newline, lines) + (trailingNewline ? newline : string.Empty));
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static List<string> Split(string content, out string newline, out bool trailingNewline)
    {
        newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (trailingNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static bool TryTimestamp(string line, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions);
            if (entry?.Kind == "cycle" && entry.Cycle is not null)
            {
                timestamp = entry.Cycle.CompletedAt.ToUniversalTime();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }
}
