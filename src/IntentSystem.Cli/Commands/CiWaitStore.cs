using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Durable, append-only CI wait obligations. A wait is an observation, not a
/// poller: recording it does not start a process and clearing it is owned by
/// the canonical PR transition command.
/// </summary>
internal static class CiWaitStore
{
    private const string FileName = "ci-waits.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolvePath(string repoRoot) =>
        Path.Combine(Path.GetFullPath(repoRoot), ".intent-cli", "automation", FileName);

    public static CiWaitReadResult ReadOpen(string repoRoot, string? domain = null, string? repo = null)
    {
        var path = ResolvePath(repoRoot);
        if (!File.Exists(path))
        {
            return new CiWaitReadResult(Array.Empty<CiWaitRecord>(), path, null);
        }

        var open = new Dictionary<string, CiWaitRecord>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<CiWaitEvent>(rawLine, JsonOptions)
                    ?? throw new InvalidOperationException("CI wait event was empty.");
                var key = Key(entry.Repo, entry.Pr);
                if (string.Equals(entry.Kind, "record", StringComparison.Ordinal))
                {
                    if (entry.Record is null)
                    {
                        throw new InvalidOperationException("CI wait record event did not contain a record.");
                    }

                    open[key] = entry.Record;
                }
                else if (string.Equals(entry.Kind, "clear", StringComparison.Ordinal))
                {
                    open.Remove(key);
                }
                else
                {
                    throw new InvalidOperationException($"Unknown CI wait event kind '{entry.Kind}'.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new CiWaitReadResult(Array.Empty<CiWaitRecord>(), path, exception.Message);
        }

        var records = open.Values
            .Where(item => (domain is null || string.Equals(item.Domain, domain, StringComparison.OrdinalIgnoreCase))
                && (repo is null || string.Equals(item.Repo, repo, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Repo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Pr)
            .ToArray();
        return new CiWaitReadResult(records, path, null);
    }

    public static CiWaitWriteResult Record(string repoRoot, CiWaitRecord record, bool write)
    {
        var path = ResolvePath(repoRoot);
        var existing = ReadOpen(repoRoot, record.Domain, record.Repo);
        if (existing.Error is not null)
        {
            return new CiWaitWriteResult(false, false, path, existing.Error);
        }

        var current = existing.Records.FirstOrDefault(item => item.Pr == record.Pr);
        if (current is not null)
        {
            if (string.Equals(current.ObservedHead, record.ObservedHead, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.OwedTransition, record.OwedTransition, StringComparison.Ordinal))
            {
                return new CiWaitWriteResult(false, true, path, null);
            }

            // A PR head can legitimately move while the same transition is
            // still owed.  Keep the store append-only, but let the canonical
            // stale-head remedy advance the observation to that new exact
            // head.  A different transition remains a conflict: replacing
            // it would erase an unrelated lifecycle obligation.
            if (string.Equals(current.OwedTransition, record.OwedTransition, StringComparison.Ordinal))
            {
                if (!write)
                {
                    return new CiWaitWriteResult(false, false, path, null);
                }

                try
                {
                    Append(path, new CiWaitEvent
                    {
                        Kind = "record",
                        Repo = record.Repo,
                        Pr = record.Pr,
                        Record = record,
                    });
                    return new CiWaitWriteResult(true, false, path, null);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return new CiWaitWriteResult(false, false, path, exception.Message);
                }
            }

            return new CiWaitWriteResult(
                false,
                false,
                path,
                $"PR #{record.Pr} already has an open CI wait for head '{current.ObservedHead}' and transition "
                + $"'{current.OwedTransition}'; refusing to overwrite it with head '{record.ObservedHead}' / "
                + $"'{record.OwedTransition}'. Re-read the exact head, clear the old transition, or record the "
                + "new obligation under the canonical lifecycle.");
        }

        if (!write)
        {
            return new CiWaitWriteResult(false, false, path, null);
        }

        try
        {
            Append(path, new CiWaitEvent
            {
                Kind = "record",
                Repo = record.Repo,
                Pr = record.Pr,
                Record = record,
            });
            return new CiWaitWriteResult(true, false, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CiWaitWriteResult(false, false, path, exception.Message);
        }
    }

    public static CiWaitWriteResult ClearForTransition(string repoRoot, string repo, int pr, string transition, bool write = true)
    {
        var path = ResolvePath(repoRoot);
        var existing = ReadOpen(repoRoot, repo: repo);
        if (existing.Error is not null)
        {
            return new CiWaitWriteResult(false, false, path, existing.Error);
        }

        var current = existing.Records.FirstOrDefault(item => item.Pr == pr
            && string.Equals(item.OwedTransition, transition, StringComparison.Ordinal));
        if (current is null)
        {
            return new CiWaitWriteResult(false, true, path, null);
        }

        if (!write)
        {
            return new CiWaitWriteResult(false, false, path, null);
        }

        try
        {
            Append(path, new CiWaitEvent
            {
                Kind = "clear",
                Repo = repo,
                Pr = pr,
                Transition = transition,
            });
            return new CiWaitWriteResult(true, false, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CiWaitWriteResult(false, false, path, exception.Message);
        }
    }

    private static void Append(string path, CiWaitEvent entry)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("CI wait store path did not contain a directory.");
        Directory.CreateDirectory(directory);
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Key(string repo, int pr) => $"{repo}#{pr}";
}

internal sealed record CiWaitRecord
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("observed_head")]
    public required string ObservedHead { get; init; }

    [JsonPropertyName("owed_transition")]
    public required string OwedTransition { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }
}

internal sealed record CiWaitEvent
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("transition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Transition { get; init; }

    [JsonPropertyName("record")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CiWaitRecord? Record { get; init; }
}

internal sealed record CiWaitReadResult(
    IReadOnlyList<CiWaitRecord> Records,
    string Path,
    string? Error);

internal sealed record CiWaitWriteResult(
    bool Applied,
    bool AlreadyConverged,
    string Path,
    string? Error);
