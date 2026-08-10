using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G662: append-only evidence that a human/agent improve realignment review
/// ran. The record deliberately carries no verdict or score: the CLI can
/// measure recency, but it cannot grade the semantic quality of the review.
/// </summary>
internal static class ImproveRunStore
{
    public const string DirectoryName = "improve";
    public const string FileName = "runs.jsonl";

    private static readonly object Sync = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static Func<string, string, ImproveRunWriteResult>? WriteOverride { get; set; }

    public static string ResolvePath(string artifactRoot, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ValidateDomain(domain);
        return Path.GetFullPath(Path.Combine(artifactRoot, DirectoryName, domain, FileName));
    }

    public static ImproveRunReadResult ReadLatest(string artifactRoot, string domain)
    {
        string path;
        try
        {
            path = ResolvePath(artifactRoot, domain);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ImproveRunReadResult
            {
                Resolved = false,
                Path = artifactRoot,
                Error = exception.Message,
            };
        }

        lock (Sync)
        {
            if (!File.Exists(path))
            {
                return new ImproveRunReadResult
                {
                    Resolved = true,
                    Path = path,
                };
            }

            try
            {
                ImproveRunRecord? latest = null;
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    latest = JsonSerializer.Deserialize<ImproveRunRecord>(line, JsonOptions)
                        ?? throw new InvalidDataException($"Improve run line {lineNumber} was empty.");
                    if (!string.Equals(latest.Domain, domain, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Improve run line {lineNumber} declares domain '{latest.Domain}', expected '{domain}'.");
                    }
                }

                return new ImproveRunReadResult
                {
                    Resolved = true,
                    Path = path,
                    Latest = latest,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return new ImproveRunReadResult
                {
                    Resolved = false,
                    Path = path,
                    Error = $"Improve run state at '{path}' could not be read: {exception.Message}",
                };
            }
        }
    }

    public static ImproveRunWriteResult Append(string artifactRoot, ImproveRunRecord record, bool write)
    {
        string path;
        try
        {
            path = ResolvePath(artifactRoot, record.Domain);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ImproveRunWriteResult(false, artifactRoot, exception.Message);
        }

        if (!write)
        {
            return new ImproveRunWriteResult(false, path, null);
        }

        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return new ImproveRunWriteResult(true, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new ImproveRunWriteResult(false, path, exception.Message);
            }
        }
    }

    internal static void ValidateDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)
            || domain.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || domain is "." or "..")
        {
            throw new ArgumentException($"Improve domain '{domain}' is not a safe path segment.", nameof(domain));
        }
    }
}

internal sealed record ImproveRunRecord
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    [JsonPropertyName("touched_artifacts")]
    public required IReadOnlyList<string> TouchedArtifacts { get; init; }

}

internal sealed record ImproveRunReadResult
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public ImproveRunRecord? Latest { get; init; }
    public string? Error { get; init; }
}

internal sealed record ImproveRunWriteResult(bool Applied, string Path, string? Error);

/// <summary>
/// G662: a declared realignment window is durable configuration, analogous to
/// the supervision bound and independent of run evidence. That separation lets
/// guide next surface a lapsed declaration even before the first run.
/// </summary>
internal static class ImproveRealignmentWindowStore
{
    public const string FileName = "window.json";

    public static string ResolvePath(string artifactRoot, string domain)
    {
        ImproveRunStore.ValidateDomain(domain);
        return Path.GetFullPath(Path.Combine(artifactRoot, ImproveRunStore.DirectoryName, domain, FileName));
    }

    public static ImproveWindowReadResult Read(string artifactRoot, string domain)
    {
        string path;
        try
        {
            path = ResolvePath(artifactRoot, domain);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ImproveWindowReadResult
            {
                Resolved = false,
                Path = artifactRoot,
                Error = exception.Message,
            };
        }
        if (!File.Exists(path))
        {
            return new ImproveWindowReadResult { Resolved = true, Path = path };
        }

        try
        {
            var record = JsonSerializer.Deserialize<ImproveWindowRecord>(File.ReadAllText(path), ImproveRunStore.JsonOptions)
                ?? throw new InvalidDataException("Improve realignment window record was empty.");
            if (!string.Equals(record.Domain, domain, StringComparison.Ordinal) || record.WindowDays <= 0)
            {
                throw new InvalidDataException("Improve realignment window record has an invalid domain or window_days value.");
            }
            return new ImproveWindowReadResult { Resolved = true, Path = path, Record = record };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new ImproveWindowReadResult
            {
                Resolved = false,
                Path = path,
                Error = $"Improve realignment window at '{path}' could not be read: {exception.Message}",
            };
        }
    }

    public static ImproveRunWriteResult Write(string artifactRoot, ImproveWindowRecord record, bool write)
    {
        string path;
        try
        {
            path = ResolvePath(artifactRoot, record.Domain);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ImproveRunWriteResult(false, artifactRoot, exception.Message);
        }
        if (!write)
        {
            return new ImproveRunWriteResult(false, path, null);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(record, ImproveRunStore.JsonOptions) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new ImproveRunWriteResult(true, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ImproveRunWriteResult(false, path, exception.Message);
        }
    }
}

internal sealed record ImproveWindowRecord
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("window_days")]
    public required int WindowDays { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }
}

internal sealed record ImproveWindowReadResult
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public ImproveWindowRecord? Record { get; init; }
    public string? Error { get; init; }
}
