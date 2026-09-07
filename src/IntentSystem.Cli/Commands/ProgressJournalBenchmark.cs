using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Production-owned journal envelope and cursor benchmark used by the G812
/// rollout gate. It writes the journal, reopens it, and measures bytes and
/// elapsed scan time; callers never supply the measured values as evidence.
/// </summary>
internal sealed record ProgressJournalBenchmarkOptions
{
    public int RecordsPerHistory { get; init; } = 108_474;
    public long MinimumCycleBytes { get; init; } = 130L * 1024 * 1024;
    public long MinimumStallBytes { get; init; } = 63L * 1024 * 1024;
    public int SteadySweeps { get; init; } = 100;
    public int ColdRestartSweeps { get; init; } = 3;
    public int CursorOverlapRecords { get; init; } = 1;
}

internal sealed record ProgressJournalBenchmarkResult
{
    public required string CyclesPath { get; init; }
    public required string StallsPath { get; init; }
    public long CycleRecords { get; init; }
    public long CycleBytes { get; init; }
    public long StallRecords { get; init; }
    public long StallBytes { get; init; }
    public long FirstHistoryBytes { get; init; }
    public long SecondHistoryBytes { get; init; }
    public long FirstHistoryRecords { get; init; }
    public long SecondHistoryRecords { get; init; }
    public long DeltaRecords { get; init; }
    public long CursorRecordsRead { get; init; }
    public long ParsedBytes { get; init; }
    public long ReadBytes { get; init; }
    public int SteadySweeps { get; init; }
    public int ColdRestartSweeps { get; init; }
    public bool IdenticalDelta { get; init; }
    public double MaxPSSeconds { get; init; }
    public long WriteElapsedMilliseconds { get; init; }
    public long ScanElapsedMilliseconds { get; init; }
}

internal static class ProgressJournalBenchmark
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static ProgressJournalBenchmarkResult Run(
        string root,
        ProgressJournalBenchmarkOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        options ??= new ProgressJournalBenchmarkOptions();
        if (options.RecordsPerHistory <= 0 || options.SteadySweeps <= 0 || options.ColdRestartSweeps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "journal benchmark dimensions must be positive");
        }

        Directory.CreateDirectory(root);
        var cyclesPath = Path.Combine(root, "cycles.jsonl");
        var stallsPath = Path.Combine(root, "stalls.jsonl");
        var writeClock = Stopwatch.StartNew();
        var cycleWrite = WriteJournal(cyclesPath, options.RecordsPerHistory, payloadBytes: 768, "cycle");
        var stallWrite = WriteJournal(stallsPath, options.RecordsPerHistory, payloadBytes: 384, "stall");
        writeClock.Stop();

        var scanClock = Stopwatch.StartNew();
        var cycleFirst = Scan(cyclesPath, 0, options.RecordsPerHistory);
        var cycleSecond = Scan(cyclesPath, cycleFirst.ByteOffsetAfterHistory, options.RecordsPerHistory);
        var stallFirst = Scan(stallsPath, 0, options.RecordsPerHistory);
        var stallSecond = Scan(stallsPath, stallFirst.ByteOffsetAfterHistory, options.RecordsPerHistory);
        var cursorOffset = cycleWrite.FirstHistoryStartOffset +
            Math.Max(0, options.RecordsPerHistory - options.CursorOverlapRecords) * cycleWrite.LineBytes;
        var cursor = Scan(cyclesPath, cursorOffset);

        var maxP = 0d;
        for (var i = 0; i < options.SteadySweeps; i++)
        {
            var sweep = Stopwatch.StartNew();
            _ = Scan(cyclesPath, cursorOffset + cycleWrite.LineBytes * Math.Max(0, options.RecordsPerHistory - 1), options.CursorOverlapRecords + 1);
            sweep.Stop();
            maxP = Math.Max(maxP, sweep.Elapsed.TotalSeconds);
        }

        for (var i = 0; i < options.ColdRestartSweeps; i++)
        {
            using var stream = new FileStream(cyclesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sweep = Stopwatch.StartNew();
            _ = Scan(stream, 0);
            sweep.Stop();
            maxP = Math.Max(maxP, sweep.Elapsed.TotalSeconds);
        }

        scanClock.Stop();
        var firstDeltaBytes = cycleSecond.BytesRead;
        var secondDeltaBytes = stallSecond.BytesRead;
        var identicalDelta = cycleFirst.Records == cycleSecond.Records
            && cycleFirst.BytesRead == firstDeltaBytes
            && stallFirst.Records == stallSecond.Records
            && stallFirst.BytesRead == secondDeltaBytes;
        return new ProgressJournalBenchmarkResult
        {
            CyclesPath = cyclesPath,
            StallsPath = stallsPath,
            CycleRecords = cycleFirst.Records + cycleSecond.Records,
            CycleBytes = cycleFirst.BytesRead + cycleSecond.BytesRead,
            StallRecords = stallFirst.Records + stallSecond.Records,
            StallBytes = stallFirst.BytesRead + stallSecond.BytesRead,
            FirstHistoryBytes = cycleFirst.BytesRead,
            SecondHistoryBytes = firstDeltaBytes,
            FirstHistoryRecords = cycleFirst.Records,
            SecondHistoryRecords = cycleSecond.Records,
            DeltaRecords = cycleSecond.Records,
            CursorRecordsRead = cursor.Records,
            ParsedBytes = cursor.BytesRead + cycleFirst.BytesRead + cycleSecond.BytesRead + stallFirst.BytesRead + stallSecond.BytesRead,
            ReadBytes = cursor.BytesRead + cycleFirst.BytesRead + cycleSecond.BytesRead + stallFirst.BytesRead + stallSecond.BytesRead,
            SteadySweeps = options.SteadySweeps,
            ColdRestartSweeps = options.ColdRestartSweeps,
            IdenticalDelta = identicalDelta,
            MaxPSSeconds = maxP,
            WriteElapsedMilliseconds = writeClock.ElapsedMilliseconds,
            ScanElapsedMilliseconds = scanClock.ElapsedMilliseconds,
        };
    }

    private static JournalWriteResult WriteJournal(string path, int recordsPerHistory, int payloadBytes, string kind)
    {
        var lineBytes = 0L;
        var firstHistoryStart = 0L;
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, Utf8, bufferSize: 64 * 1024, leaveOpen: true))
        {
            for (var history = 0; history < 2; history++)
            {
                if (history == 1) firstHistoryStart = 0;
                for (var index = 0; index < recordsPerHistory; index++)
                {
                    var sequence = index.ToString("D8", CultureInfo.InvariantCulture);
                    var line = $"{{\"kind\":\"{kind}\",\"history\":\"{history:D1}\",\"sequence\":\"{sequence}\",\"payload\":\"{new string('x', payloadBytes)}\"}}\n";
                    writer.Write(line);
                    if (lineBytes == 0) lineBytes = Utf8.GetByteCount(line);
                }
            }
            writer.Flush();
            stream.Flush(flushToDisk: false);
        }
        return new JournalWriteResult(firstHistoryStart, lineBytes);
    }

    private static JournalScanResult Scan(string path, long offset, long? maxRecords = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Scan(stream, offset, maxRecords);
    }

    private static JournalScanResult Scan(FileStream stream, long offset, long? maxRecords = null)
    {
        if (offset > stream.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
        long records = 0;
        long bytes = 0;
        while ((!maxRecords.HasValue || records < maxRecords.Value) && reader.ReadLine() is { } line)
        {
            records++;
            bytes += Utf8.GetByteCount(line) + 1;
        }
        return new JournalScanResult(records, bytes, offset + bytes);
    }

    private readonly record struct JournalWriteResult(long FirstHistoryStartOffset, long LineBytes);

    private readonly record struct JournalScanResult(long Records, long BytesRead, long ByteOffsetAfterHistory);
}
