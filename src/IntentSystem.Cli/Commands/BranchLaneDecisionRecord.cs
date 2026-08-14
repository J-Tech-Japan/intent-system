using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal abstract record BranchLaneDecisionRecord
{
    [JsonPropertyName("record_kind")]
    public required string RecordKind { get; init; }
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }
    [JsonPropertyName("lane_id")]
    public required string LaneId { get; init; }
    [JsonPropertyName("start_branch")]
    public required string StartBranch { get; init; }
    [JsonPropertyName("pr_base_branch")]
    public required string PrBaseBranch { get; init; }
    [JsonPropertyName("landing_mode")]
    public required string LandingMode { get; init; }
    [JsonPropertyName("definition_revision")]
    public required string DefinitionRevision { get; init; }
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }
    [JsonPropertyName("actor_role")]
    public required string ActorRole { get; init; }
    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }
    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; init; }

    /// <summary>
    /// G692: recorded only when an authoring-only operator confirmation is
    /// used. Null preserves the G669 delivery record shape.
    /// </summary>
    [JsonPropertyName("team_mode")]
    public string? TeamMode { get; init; }
}

internal sealed record BranchLaneProposeRecord : BranchLaneDecisionRecord
{
    public const string Kind = "branch-lane-propose";
    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }
}

internal sealed record BranchLaneConfirmRecord : BranchLaneDecisionRecord
{
    public const string Kind = "branch-lane-confirm";
}

internal sealed record BranchLaneDecisionReadResult<T>
    where T : BranchLaneDecisionRecord
{
    public required string Path { get; init; }
    public T? Record { get; init; }
    public string? Error { get; init; }
}

internal static class BranchLaneDecisionStore
{
    public const string RecordRootRelativePath = ".intent-cli/branch-lane-decisions";
    public const string ProposeFileName = "propose.json";
    public const string ConfirmFileName = "confirm.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolveRelativePath(string executionUnit, bool confirmation)
    {
        ValidateExecutionUnit(executionUnit);
        return $"{RecordRootRelativePath}/{executionUnit}/{(confirmation ? ConfirmFileName : ProposeFileName)}";
    }

    public static string ResolveFullPath(string repoRoot, string executionUnit, bool confirmation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ValidateExecutionUnit(executionUnit);
        var root = Path.GetFullPath(Path.Combine(repoRoot, RecordRootRelativePath));
        var path = Path.GetFullPath(Path.Combine(root, executionUnit, confirmation ? ConfirmFileName : ProposeFileName));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"resolved lane decision path for '{executionUnit}' escapes the record root.");
        }
        return path;
    }

    public static BranchLaneDecisionReadResult<BranchLaneProposeRecord> ReadPropose(string repoRoot, string executionUnit) =>
        Read<BranchLaneProposeRecord>(repoRoot, executionUnit, false);

    public static BranchLaneDecisionReadResult<BranchLaneConfirmRecord> ReadConfirm(string repoRoot, string executionUnit) =>
        Read<BranchLaneConfirmRecord>(repoRoot, executionUnit, true);

    public static string Serialize(BranchLaneDecisionRecord record) =>
        JsonSerializer.Serialize(record, record.GetType(), JsonOptions);

    public static BranchLaneDecisionWriteResult Write(
        string repoRoot,
        string executionUnit,
        BranchLaneDecisionRecord record,
        bool confirmation)
    {
        ArgumentNullException.ThrowIfNull(record);
        var path = ResolveFullPath(repoRoot, executionUnit, confirmation);
        try
        {
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("lane decision record path has no parent directory.");
            Directory.CreateDirectory(directory);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(Serialize(record));
            return new BranchLaneDecisionWriteResult { Succeeded = true };
        }
        catch (IOException exception)
        {
            return new BranchLaneDecisionWriteResult
            {
                Succeeded = false,
                Error = $"could not write lane decision record at {ResolveRelativePath(executionUnit, confirmation)}: {exception.Message}",
            };
        }
    }

    public static string ComputeFingerprint(BranchRoutingSnapshot snapshot) =>
        ComputeFingerprint(snapshot.LaneId, snapshot.DefinitionRevision, snapshot.StartBranch, snapshot.PrBaseBranch, snapshot.LandingMode);

    public static string ComputeFingerprint(string laneId, string definitionRevision, string startBranch, string prBaseBranch, string landingMode)
    {
        var canonical = string.Join("|", laneId.Trim(), definitionRevision.Trim(), startBranch.Trim(), prBaseBranch.Trim(), landingMode.Trim());
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static bool ValidateRecordMatches(BranchLaneDecisionRecord record, BranchRoutingSnapshot snapshot, out string error)
    {
        var expected = ComputeFingerprint(snapshot);
        var differences = new List<string>();
        AddDifference(differences, "lane_id", record.LaneId, snapshot.LaneId);
        AddDifference(differences, "definition_revision", record.DefinitionRevision, snapshot.DefinitionRevision);
        AddDifference(differences, "start_branch", record.StartBranch, snapshot.StartBranch);
        AddDifference(differences, "pr_base_branch", record.PrBaseBranch, snapshot.PrBaseBranch);
        AddDifference(differences, "landing_mode", record.LandingMode, snapshot.LandingMode);
        if (!string.Equals(record.Fingerprint, expected, StringComparison.Ordinal))
        {
            differences.Add($"fingerprint='{record.Fingerprint}' (expected '{expected}')");
        }
        error = differences.Count == 0 ? string.Empty : string.Join("; ", differences);
        return differences.Count == 0;
    }

    public static bool ValidatePair(
        BranchLaneProposeRecord propose,
        BranchLaneConfirmRecord confirm,
        BranchRoutingSnapshot snapshot,
        out string error)
        => ValidatePair(propose, confirm, snapshot, "orchestration", expectedTeamMode: null, out error);

    public static bool ValidatePair(
        BranchLaneProposeRecord propose,
        BranchLaneConfirmRecord confirm,
        BranchRoutingSnapshot snapshot,
        string expectedConfirmRole,
        string? expectedTeamMode,
        out string error)
    {
        var problems = new List<string>();
        if (!ValidateRecordMatches(propose, snapshot, out var proposeError))
        {
            problems.Add($"propose record: {proposeError}");
        }
        if (!ValidateRecordMatches(confirm, snapshot, out var confirmError))
        {
            problems.Add($"confirm record: {confirmError}");
        }
        if (string.Equals(propose.Actor, confirm.Actor, StringComparison.Ordinal))
        {
            problems.Add($"propose actor '{propose.Actor}' and confirm actor '{confirm.Actor}' are identical");
        }
        if (!string.Equals(propose.ActorRole, "design", StringComparison.Ordinal))
        {
            problems.Add($"propose actor_role is '{propose.ActorRole}', expected 'design'");
        }
        if (!string.Equals(confirm.ActorRole, expectedConfirmRole, StringComparison.Ordinal))
        {
            problems.Add($"confirm actor_role is '{confirm.ActorRole}', expected '{expectedConfirmRole}'");
        }
        if (expectedTeamMode is not null)
        {
            if (!string.Equals(propose.TeamMode, expectedTeamMode, StringComparison.Ordinal)
                || !string.Equals(confirm.TeamMode, expectedTeamMode, StringComparison.Ordinal))
            {
                problems.Add(
                    $"authoring lane records must both record team_mode '{expectedTeamMode}'");
            }
        }
        else if (propose.TeamMode is not null
            && !TeamMode.IsKnown(propose.TeamMode)
            || confirm.TeamMode is not null && !TeamMode.IsKnown(confirm.TeamMode))
        {
            problems.Add("lane decision records contain an unknown team_mode");
        }
        if (confirm.RecordedAt < propose.RecordedAt)
        {
            problems.Add($"confirm recorded_at '{confirm.RecordedAt:O}' precedes propose recorded_at '{propose.RecordedAt:O}'");
        }
        if (string.IsNullOrWhiteSpace(propose.Rationale))
        {
            problems.Add("propose rationale is empty");
        }
        if (string.IsNullOrWhiteSpace(propose.Evidence))
        {
            problems.Add("propose evidence is empty");
        }
        if (string.IsNullOrWhiteSpace(confirm.Evidence))
        {
            problems.Add("confirm evidence is empty");
        }
        error = string.Join("; ", problems);
        return problems.Count == 0;
    }

    private static BranchLaneDecisionReadResult<T> Read<T>(string repoRoot, string executionUnit, bool confirmation)
        where T : BranchLaneDecisionRecord
    {
        var path = ResolveFullPath(repoRoot, executionUnit, confirmation);
        if (!File.Exists(path))
        {
            return new BranchLaneDecisionReadResult<T> { Path = path, Record = null, Error = null };
        }
        try
        {
            var record = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
            if (record is null)
            {
                return new BranchLaneDecisionReadResult<T> { Path = path, Record = null, Error = "record deserialized to null" };
            }
            var expectedKind = confirmation ? BranchLaneConfirmRecord.Kind : BranchLaneProposeRecord.Kind;
            var error = ValidateShape(record, expectedKind, executionUnit);
            return new BranchLaneDecisionReadResult<T>
            {
                Path = path,
                Record = error is null ? record : null,
                Error = error,
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
        {
            return new BranchLaneDecisionReadResult<T>
            {
                Path = path,
                Record = null,
                Error = $"record is not valid JSON: {exception.Message}",
            };
        }
    }

    private static string? ValidateShape(BranchLaneDecisionRecord record, string expectedKind, string executionUnit)
    {
        var problems = new List<string>();
        if (!string.Equals(record.RecordKind, expectedKind, StringComparison.Ordinal))
        {
            problems.Add($"record_kind is '{record.RecordKind}', expected '{expectedKind}'");
        }
        if (!string.Equals(record.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            problems.Add($"execution_unit is '{record.ExecutionUnit}', expected '{executionUnit}'");
        }
        foreach (var (name, value) in new[]
        {
            ("lane_id", record.LaneId),
            ("start_branch", record.StartBranch),
            ("pr_base_branch", record.PrBaseBranch),
            ("landing_mode", record.LandingMode),
            ("definition_revision", record.DefinitionRevision),
            ("actor", record.Actor),
            ("actor_role", record.ActorRole),
            ("evidence", record.Evidence),
            ("fingerprint", record.Fingerprint),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{name} is empty");
            }
        }
        if (record.RecordedAt == default)
        {
            problems.Add("recorded_at is missing");
        }
        if (record.TeamMode is not null && !TeamMode.IsKnown(record.TeamMode))
        {
            problems.Add($"team_mode is '{record.TeamMode}', expected delivery or authoring-only");
        }
        if (record is BranchLaneProposeRecord propose && string.IsNullOrWhiteSpace(propose.Rationale))
        {
            problems.Add("rationale is empty");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    private static void AddDifference(List<string> differences, string name, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            differences.Add($"{name}='{actual}' (expected '{expected}')");
        }
    }

    private static void ValidateExecutionUnit(string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }
}

internal sealed record BranchLaneDecisionWriteResult
{
    public required bool Succeeded { get; init; }
    public string? Error { get; init; }
}

internal sealed record BranchLaneDecisionGateResult
{
    public required bool Legacy { get; init; }
    public required bool Passed { get; init; }
    public string? ProposeRecordPath { get; init; }
    public string? ConfirmRecordPath { get; init; }
    public string? Error { get; init; }
}

internal static class BranchLaneDecisionGate
{
    public static BranchLaneDecisionGateResult Evaluate(string repoRoot, string executionUnit)
        => Evaluate(repoRoot, executionUnit, teamMode: null);

    public static BranchLaneDecisionGateResult Evaluate(
        string repoRoot,
        string executionUnit,
        string? teamMode)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var executionUnitError))
        {
            return Failure($"invalid execution unit '{executionUnit}': {executionUnitError}");
        }

        var packetPath = Path.Combine(repoRoot, ".intent-cli", "issues", executionUnit, "packet.yaml");
        if (!File.Exists(packetPath))
        {
            return LegacyResult();
        }
        PacketYamlDocument? document;
        try
        {
            if (!PacketYamlDocument.TryParse(File.ReadAllText(packetPath), out document, out var parseError) || document is null)
            {
                return Failure($"could not parse packet.yaml: {parseError}");
            }
        }
        catch (IOException exception)
        {
            return Failure($"could not read packet.yaml: {exception.Message}");
        }
        var lane = BranchLaneResolver.TryReadDeclaredLane(document.Fields);
        if (string.IsNullOrWhiteSpace(lane))
        {
            return LegacyResult();
        }
        BranchRoutingSnapshot snapshot;
        try
        {
            snapshot = BranchLaneResolver.TryReadSnapshot(document.Fields)
                ?? throw new InvalidOperationException($"packet declares branch_lane '{lane}' without a complete routing_snapshot");
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception.Message);
        }
        var propose = BranchLaneDecisionStore.ReadPropose(repoRoot, executionUnit);
        var confirm = BranchLaneDecisionStore.ReadConfirm(repoRoot, executionUnit);
        var missing = new List<string>();
        if (propose.Record is null) missing.Add($"propose missing ({propose.Error ?? propose.Path})");
        if (confirm.Record is null) missing.Add($"confirm missing ({confirm.Error ?? confirm.Path})");
        if (missing.Count > 0)
        {
            return Failure($"lane '{snapshot.LaneId}' requires separate propose and confirm records: {string.Join("; ", missing)}");
        }
        var authoringOnly = TeamMode.IsAuthoringOnly(teamMode);
        var expectedConfirmRole = authoringOnly ? "operator" : "orchestration";
        var expectedTeamMode = authoringOnly ? TeamMode.AuthoringOnly : null;
        if (!BranchLaneDecisionStore.ValidatePair(
                propose.Record!,
                confirm.Record!,
                snapshot,
                expectedConfirmRole,
                expectedTeamMode,
                out var pairError))
        {
            return Failure($"lane decision records are invalid: {pairError}");
        }
        return new BranchLaneDecisionGateResult
        {
            Legacy = false,
            Passed = true,
            ProposeRecordPath = propose.Path,
            ConfirmRecordPath = confirm.Path,
        };
    }

    private static BranchLaneDecisionGateResult LegacyResult() => new() { Legacy = true, Passed = true };
    private static BranchLaneDecisionGateResult Failure(string error) => new() { Legacy = false, Passed = false, Error = error };
}
