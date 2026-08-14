using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G564: the canonical, machine-readable record that a packet-declared
/// knowledge write-back was PERFORMED, carrying the host commit as evidence.
///
/// The write-back itself is design's host-side act (G300 stands — nothing here
/// writes intent content). What was missing was any durable statement that it
/// happened: the evidence lived only in host commits the detection layer
/// cannot see, so nothing could say "not done" and the tree fell weeks behind
/// development with no structural signal.
///
/// The compatibility record remains at
/// <c>.intent-cli/knowledge-writebacks/&lt;unit&gt;/record.json</c>. Explicit
/// G698 recorder roles use
/// <c>.intent-cli/knowledge-writebacks/&lt;unit&gt;/records/&lt;role&gt;.json</c>,
/// written only by <see cref="AutomationKnowledgeWriteBackRecordCommand"/>.
/// Legacy single records remain readable and unattributed; they are never
/// rewritten merely to add a role.
/// </summary>
internal sealed record KnowledgeWriteBackRecord
{
    public const string ArtifactKindValue = "knowledge-writeback-record";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    [JsonPropertyName("artifact_kind")]
    public required string ArtifactKind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    /// <summary>
    /// G698 recorder attribution. Missing is intentional for a pre-G698
    /// legacy artifact and means unattributed; newly written records carry
    /// <c>design</c> or <c>orchestration</c>.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    /// <summary>The host commit the write-back landed in — the evidence.</summary>
    [JsonPropertyName("host_commit")]
    public required string HostCommit { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>
    /// Paths actually written, as reported by the recorder. Optional: the
    /// packet's declared targets remain the contract, and a recorder that
    /// names nothing still produces a valid record.
    /// </summary>
    [JsonPropertyName("targets")]
    public required IReadOnlyList<string> Targets { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>G564 review repair: the artifact root every record lives beneath.</summary>
    public const string RecordRootRelativePath = ".intent-cli/knowledge-writebacks";

    /// <summary>G564 review repair: the packet root every declaration is read from.</summary>
    public const string PacketRootRelativePath = ".intent-cli/issues";

    /// <summary>Shortest / longest evidence a commit SHA may be.</summary>
    public const int MinimumCommitLength = 7;

    public const int MaximumCommitLength = 40;

    /// <summary>
    /// G564 review repair: an execution unit is a canonical IDENTIFIER, and this
    /// feature interpolates it into two filesystem paths. Without this gate,
    /// <c>--execution-unit ../../.intent-cli/issues/G564</c> resolved a record
    /// path OUTSIDE the artifact root, and a write would have escaped it.
    ///
    /// The rule is an allow-list rather than a blocklist of dangerous sequences:
    /// letters, digits, <c>-</c>, <c>_</c> and <c>.</c>, never starting with
    /// <c>.</c> and never containing <c>..</c>. That admits every real unit id
    /// (<c>G564</c>, <c>SKS-G818</c>, <c>v1.2</c>) and structurally excludes
    /// separators, rooted paths, drive/ADS colons, dot-segments, whitespace, and
    /// control characters — so the classes of input the reviewer found cannot be
    /// enumerated wrongly.
    /// </summary>
    public static bool TryValidateExecutionUnit(string? executionUnit, out string error)
    {
        const int MaximumLength = 128;

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "execution unit is required and must not be blank.";
            return false;
        }

        if (executionUnit.Length > MaximumLength)
        {
            error = $"execution unit '{executionUnit}' exceeds {MaximumLength} characters.";
            return false;
        }

        if (executionUnit[0] == '.')
        {
            error =
                $"execution unit '{executionUnit}' starts with '.', which is not a canonical identifier "
                + "(a leading dot introduces a relative path segment).";
            return false;
        }

        if (executionUnit.Contains("..", StringComparison.Ordinal))
        {
            error =
                $"execution unit '{executionUnit}' contains '..' — a dot-segment is a path traversal, never part "
                + "of an execution unit id.";
            return false;
        }

        foreach (var character in executionUnit)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                error =
                    $"execution unit '{executionUnit}' contains '{character}', which is not allowed. Canonical "
                    + "execution unit ids use ASCII letters, digits, '-', '_' and '.' only — never path separators, "
                    + "drive letters, or whitespace.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>True when <paramref name="value"/> is a 7–40 character hexadecimal SHA.</summary>
    public static bool IsCommitShaped(string? value) =>
        value is not null
        && value.Length >= MinimumCommitLength
        && value.Length <= MaximumCommitLength
        && value.All(Uri.IsHexDigit);

    /// <summary>
    /// Repo-relative artifact path for <paramref name="executionUnit"/>,
    /// always in forward-slash form so it reads identically on every platform
    /// and in every report.
    /// </summary>
    public static string ResolveRelativePath(string executionUnit)
    {
        if (!TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return $"{RecordRootRelativePath}/{executionUnit}/record.json";
    }

    public static string ResolveFullPath(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        var resolved = Path.GetFullPath(Path.Combine(
            repoRoot,
            ResolveRelativePath(executionUnit).Replace('/', Path.DirectorySeparatorChar)));

        // Defense in depth: the identifier gate above already makes escape
        // impossible, so a failure here means that gate regressed — refuse
        // rather than touch a path outside the artifact root.
        return EnsureContained(repoRoot, RecordRootRelativePath, resolved, executionUnit);
    }

    /// <summary>
    /// G564 review repair: the packet a declaration is read from is resolved and
    /// contained by the same rules as the record — both paths interpolate the
    /// execution unit, so both need the same guarantee.
    /// </summary>
    public static string ResolvePacketPath(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        if (!TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var resolved = Path.GetFullPath(Path.Combine(
            repoRoot,
            PacketRootRelativePath.Replace('/', Path.DirectorySeparatorChar),
            executionUnit,
            "packet.yaml"));

        return EnsureContained(repoRoot, PacketRootRelativePath, resolved, executionUnit);
    }

    private static string EnsureContained(string repoRoot, string rootRelativePath, string resolved, string executionUnit)
    {
        var root = Path.GetFullPath(Path.Combine(
            repoRoot,
            rootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"resolved path for execution unit '{executionUnit}' escapes `{rootRelativePath}` "
                + $"({resolved}); refusing to read or write outside the artifact root.");
        }

        return resolved;
    }

    public static string Serialize(KnowledgeWriteBackRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, SerializeOptions);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> on a payload that is not
    /// a well-formed record of this kind. A record on disk that cannot be read
    /// is never treated as absent: absence means "not written back yet", and
    /// silently downgrading unreadable evidence to that would re-open the
    /// false-clearance path this artifact closes.
    ///
    /// G564 review repair: <paramref name="expectedExecutionUnit"/> is REQUIRED
    /// by every consumer. A record is only evidence for the unit it is stored
    /// under, and validation used to stop at "non-blank" — so a file at
    /// <c>…/G564/record.json</c> carrying <c>execution_unit: G999</c>, or a
    /// <c>host_commit</c> that is not a SHA at all, cleared
    /// <c>knowledge-writeback-pending</c> for G564 anyway. Both are now
    /// rejected, which surfaces them as unreadable-with-path rather than as a
    /// discharged obligation.
    /// </summary>
    public static KnowledgeWriteBackRecord Deserialize(string json, string expectedExecutionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedExecutionUnit);

        KnowledgeWriteBackRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<KnowledgeWriteBackRecord>(json, SerializeOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Knowledge write-back record is not valid JSON: {exception.Message}");
        }

        if (record is null)
        {
            throw new InvalidOperationException("Knowledge write-back record payload deserialized to null.");
        }

        if (!string.Equals(record.ArtifactKind, ArtifactKindValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Knowledge write-back record declares artifact_kind '{record.ArtifactKind}', expected '{ArtifactKindValue}'.");
        }

        if (string.IsNullOrWhiteSpace(record.ExecutionUnit) || string.IsNullOrWhiteSpace(record.HostCommit))
        {
            throw new InvalidOperationException(
                "Knowledge write-back record is missing 'execution_unit' or 'host_commit' — a record without "
                + "evidence is not a record.");
        }

        if (!string.Equals(record.ExecutionUnit, expectedExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Knowledge write-back record declares execution_unit '{record.ExecutionUnit}' but is stored for "
                + $"'{expectedExecutionUnit}'. A record is evidence only for the unit it names; a mismatched record "
                + "never discharges this unit's obligation.");
        }

        if (!IsCommitShaped(record.HostCommit))
        {
            throw new InvalidOperationException(
                $"Knowledge write-back record for '{expectedExecutionUnit}' carries host_commit "
                + $"'{record.HostCommit}', which is not a {MinimumCommitLength}-{MaximumCommitLength} character "
                + "hexadecimal SHA. Evidence a reader cannot follow to a commit is not evidence.");
        }

        if (record.Role is not null
            && !CloseoutRecordRole.TryNormalize(record.Role, out _, out var roleError))
        {
            throw new InvalidOperationException(
                $"Knowledge write-back record for '{expectedExecutionUnit}' has an invalid role: {roleError}");
        }

        // `targets: null` satisfies the required-member check but would hand
        // callers a null list; an unnamed target set is empty, not absent.
        return record with
        {
            Role = record.Role is null
                ? null
                : CloseoutRecordRole.TryNormalize(record.Role, out var normalizedRole, out _)
                    ? normalizedRole
                    : record.Role,
            Targets = record.Targets ?? Array.Empty<string>(),
        };
    }
}
