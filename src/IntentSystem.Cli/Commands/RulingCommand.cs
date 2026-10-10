using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>Explicit local record and read surfaces for supplied operator rulings.</summary>
internal static class RulingCommand
{
    private const string UsageRecord = "Usage: intent-cli ruling record --id <id> --domain <domain> --team <team> --from-file <json> --authority-role operator [--write|--dry-run] [--format json|markdown]";
    private const string UsageRead = "Usage: intent-cli ruling show|validate <id> --domain <domain> --team <team> [--format json|markdown]";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    internal static int ExecuteRecord(CliContext context, string[] args, TextWriter writer) => Execute(context, args, writer, "record");
    internal static int ExecuteRecord(CliContext context, string[] args, TextWriter writer,
        Action<string, string>? beforeOperation, DateTimeOffset? nowOverride) =>
        Execute(context, args, writer, "record", beforeOperation, nowOverride);
    internal static int ExecuteShow(CliContext context, string[] args, TextWriter writer) => Execute(context, args, writer, "show");
    internal static int ExecuteShow(CliContext context, string[] args, TextWriter writer, DateTimeOffset nowOverride) =>
        Execute(context, args, writer, "show", nowOverride: nowOverride);
    internal static int ExecuteValidate(CliContext context, string[] args, TextWriter writer) => Execute(context, args, writer, "validate");
    internal static int ExecuteValidate(CliContext context, string[] args, TextWriter writer, DateTimeOffset nowOverride) =>
        Execute(context, args, writer, "validate", nowOverride: nowOverride);

    private static int Execute(CliContext context, string[] args, TextWriter writer, string operation,
        Action<string, string>? beforeOperation = null, DateTimeOffset? nowOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        if (args.Length == 1 && args[0] == "--help") { WriteHelp(writer, operation); return 0; }
        if (!TryParse(args, operation, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(operation == "record" ? UsageRecord : UsageRead);
            return 1;
        }

        var now = nowOverride ?? DateTimeOffset.UtcNow;
        if (!RulingArtifact.TryIdentifier(parsed.Id, out var idError))
            return Emit(writer, parsed.Format, Refused(operation, parsed, now, "ruling-invalid-id", "id: " + idError));
        if (!RulingArtifact.TryIdentifier(parsed.Domain, out var domainError))
            return Emit(writer, parsed.Format, Refused(operation, parsed, now, "ruling-invalid-domain", "domain: " + domainError));
        if (!RulingArtifact.TryIdentifier(parsed.Team, out var teamError))
            return Emit(writer, parsed.Format, Refused(operation, parsed, now, "ruling-invalid-team", "team: " + teamError));

        var store = new RulingArtifactStore(context.RepoRoot, beforeOperation);
        if (operation == "record") return Record(context, store, parsed, now, writer);
        var evaluation = store.Evaluate(parsed.Domain!, parsed.Team!, parsed.Id!, now);
        var result = ResultFromEvaluation(operation, parsed, now, evaluation, operation == "show" ? "read-only" : "read-only");
        if (evaluation.Status == "missing") return Emit(writer, parsed.Format, result);
        if (evaluation.Status == "unavailable" || evaluation.Status == "conflict") return Emit(writer, parsed.Format, result);
        var record = evaluation.Records.First(x => x.Id == parsed.Id);
        result = result with
        {
            NormalizedRecord = JsonDocument.Parse(RulingArtifact.Serialize(record)).RootElement.Clone(),
            ContentSha256 = record.Sha256,
            ArtifactPath = record.RelativePath,
            TimestampSource = null,
            Expired = record.ExpiresAt is not null && now >= record.ExpiresAt.Value,
            AuthorityStatus = record.AuthorityRole == "operator" ? "supplied-not-authenticated" : "supplied-not-authenticated",
            Status = evaluation.Status,
            Cause = evaluation.Cause,
            Detail = evaluation.Detail,
            SupersededBy = evaluation.SupersededBy,
            ReplacementIds = evaluation.ReplacementIds,
        };
        if (operation == "show")
        {
            result = result with { Disposition = "shown" };
            return Emit(writer, parsed.Format, result);
        }
        result = evaluation.Status == "active"
            ? result with { Disposition = "validated" }
            : result with { Disposition = "refused", RecoveryHint = "Review the current exact-scope graph; do not rewrite existing ruling bytes." };
        return Emit(writer, parsed.Format, result);
    }

    private static int Record(CliContext context, RulingArtifactStore store, Parsed parsed, DateTimeOffset now, TextWriter writer)
    {
        var inputPath = Path.IsPathRooted(parsed.FromFile!) ? parsed.FromFile! : Path.Combine(context.RepoRoot, parsed.FromFile!);
        if (!TryValidateInputAncestors(inputPath, out var pathError))
        {
            var unavailable = Refused("record", parsed, now, "ruling-input-unavailable", pathError) with
            { Disposition = "unavailable", Status = "unavailable", RecoveryHint = "Provide a regular input file without symlink ancestors." };
            return Emit(writer, parsed.Format, unavailable);
        }
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(inputPath, RulingArtifact.MaximumBytes, out var input, out var readFailure, out var readError))
        {
            var unavailable = Refused("record", parsed, now,
                readFailure == CrossRuntimeReviewFileReadFailure.TooLarge ? "ruling-size-limit" : "ruling-input-unavailable",
                $"{readFailure}: {readError}") with
            {
                Disposition = readFailure == CrossRuntimeReviewFileReadFailure.TooLarge ? "refused" : "unavailable",
                Status = "unavailable",
                RecoveryHint = "Provide a readable regular UTF-8 JSON file within 1 MiB."
            };
            return Emit(writer, parsed.Format, unavailable);
        }
        if (!RulingArtifact.TryParse(input, false, out var supplied, out var cause, out var detail) || supplied is null)
            return Emit(writer, parsed.Format, Refused("record", parsed, now, cause, detail));

        if (supplied.Id != parsed.Id || supplied.Domain != parsed.Domain || supplied.Team != parsed.Team)
            return Emit(writer, parsed.Format, Refused("record", parsed, now, "ruling-identity-conflict", "record identity must exactly match --id, --domain and --team") with { Status = "conflict" });
        if (supplied.AuthorityRole != parsed.AuthorityRole)
            return Emit(writer, parsed.Format, Refused("record", parsed, now, "ruling-invalid-authority", "authority_role must exactly match --authority-role"));
        if (supplied.AuthorityRole != "operator")
            return Emit(writer, parsed.Format, Refused("record", parsed, now, "ruling-invalid-authority", "only the literal operator assertion is supported; it is not authenticated"));

        var initial = store.Evaluate(parsed.Domain!, parsed.Team!, parsed.Id!, now);
        if (initial.Status == "unavailable" || initial.Status == "conflict")
            return Emit(writer, parsed.Format, ResultFromEvaluation("record", parsed, now, initial, parsed.Write ? "write" : "dry-run"));

        var existing = initial.Records.FirstOrDefault(x => x.Id == parsed.Id);
        var timestampPresent = false;
        try
        {
            using var doc = JsonDocument.Parse(input);
            timestampPresent = doc.RootElement.TryGetProperty("recorded_at", out _);
        }
        catch (JsonException) { }
        var timestampSource = timestampPresent ? "supplied" : existing is not null ? "existing" : "generated";
        var record = supplied with { RecordedAt = timestampPresent ? supplied.RecordedAt : existing?.RecordedAt ?? now };
        if (record.ExpiresAt is not null && record.ExpiresAt <= record.RecordedAt)
            return Emit(writer, parsed.Format, Refused("record", parsed, now, "ruling-invalid-timestamp", "expires_at must be later than effective recorded_at"));
        var bytes = RulingArtifact.Serialize(record);
        if (bytes.Length > RulingArtifact.MaximumBytes)
            return Emit(writer, parsed.Format, Refused("record", parsed, now, "ruling-normalized-size-limit", "normalized UTF-8 record exceeds 1 MiB before any write"));
        var candidateEval = store.Evaluate(record.Domain, record.Team, record.Id, now, record, forAdmission: true);
        if (existing is not null)
        {
            var existingBytes = RulingArtifact.Serialize(existing);
            if (!existingBytes.AsSpan().SequenceEqual(bytes))
                return Emit(writer, parsed.Format, ResultFromEvaluation("record", parsed, now, candidateEval, parsed.Write ? "write" : "dry-run") with
                { Disposition = "refused", Status = "conflict", Cause = "ruling-content-conflict", Detail = "ID is immutable; supplied content differs from existing bytes.", ArtifactPath = existing.RelativePath, ContentSha256 = existing.Sha256, RecoveryHint = "Choose a new ID or record an explicit successor; existing bytes are immutable." });
            if (candidateEval.Status != "active")
                return Emit(writer, parsed.Format, ResultFromEvaluation("record", parsed, now, candidateEval, parsed.Write ? "write" : "dry-run") with
                { Disposition = "refused", Cause = candidateEval.Cause, Detail = candidateEval.Detail, ArtifactPath = existing.RelativePath, ContentSha256 = existing.Sha256, NormalizedRecord = JsonDocument.Parse(existingBytes).RootElement.Clone(), TimestampSource = timestampSource, RecoveryHint = "Review current graph status; replay does not reactivate an inactive record." });
            var idem = ResultFromEvaluation("record", parsed, now, candidateEval, parsed.Write ? "write" : "dry-run") with
            { Disposition = "idempotent", Status = "active", Cause = "", Detail = "identical active record already exists", Idempotent = true, ArtifactPath = existing.RelativePath, PlannedArtifactPath = existing.RelativePath, ContentSha256 = existing.Sha256, NormalizedRecord = JsonDocument.Parse(existingBytes).RootElement.Clone(), TimestampSource = timestampSource, Expired = false };
            return Emit(writer, parsed.Format, idem);
        }
        if (candidateEval.Status != "active")
        {
            var capacity = candidateEval.Cause == "ruling-inventory-limit";
            return Emit(writer, parsed.Format, ResultFromEvaluation("record", parsed, now, candidateEval, parsed.Write ? "write" : "dry-run") with
            {
                Disposition = candidateEval.Status == "unavailable" && candidateEval.Cause != "ruling-future-recorded-at" ? "unavailable" : "refused",
                TimestampSource = timestampSource,
                NormalizedRecord = JsonDocument.Parse(bytes).RootElement.Clone(),
                ContentSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                PlannedArtifactPath = record.RelativePath,
                RecoveryHint = capacity
                    ? "The exact-scope ruling inventory is at capacity; ask the responsible host operator to resolve scope capacity. Do not partially merge or archive records."
                    : "Review expiry and the complete exact-scope graph; no record was written."
            });
        }

        if (!parsed.Write)
        {
            var preview = ResultFromEvaluation("record", parsed, now, candidateEval, "dry-run") with
            {
                Disposition = "preview", Status = "active", TimestampSource = timestampSource,
                Detail = timestampSource == "generated" ? "A generated timestamp is a proposal, not a reservation; supply recorded_at to reproduce these bytes." : candidateEval.Detail,
                NormalizedRecord = JsonDocument.Parse(bytes).RootElement.Clone(),
                ContentSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                PlannedArtifactPath = record.RelativePath,
                RecoveryHint = null
            };
            return Emit(writer, parsed.Format, preview);
        }

        if (!store.TryWrite(record, bytes, now, out var wrote, out var idempotent, out cause, out detail, out var createdDirectories))
        {
            var current = store.Evaluate(record.Domain, record.Team, record.Id, now, record, forAdmission: true);
            var knownConflict = cause is "ruling-content-conflict" or "ruling-identity-conflict" or
                "ruling-supersession-conflict" or "ruling-incomplete-merge" or "ruling-successor-conflict";
            var storedTarget = current.Records.FirstOrDefault(x => x.Id == record.Id);
            var status = cause == "ruling-inactive-target" && current.Status is ("expired" or "superseded")
                ? current.Status
                : knownConflict ? "conflict" : "unavailable";
            var knownInactive = cause == "ruling-inactive-target" && status is ("expired" or "superseded");
            var failure = ResultFromEvaluation("record", parsed, now, current, "write") with
            {
                Disposition = wrote ? "unavailable" : knownConflict || knownInactive ? "refused" : "unavailable",
                Status = status,
                Cause = cause,
                Detail = detail,
                Wrote = wrote,
                Idempotent = idempotent,
                ContentSha256 = storedTarget?.Sha256 ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                ArtifactPath = wrote ? record.RelativePath : storedTarget?.RelativePath,
                PlannedArtifactPath = record.RelativePath,
                NormalizedRecord = JsonDocument.Parse(storedTarget is null ? bytes : RulingArtifact.Serialize(storedTarget)).RootElement.Clone(),
                CreatedDirectories = createdDirectories,
                TimestampSource = timestampSource,
                RecoveryHint = "Inspect the exact target and scoped inventory; never overwrite a ruling."
            };
            return Emit(writer, parsed.Format, failure);
        }
        var after = store.Evaluate(record.Domain, record.Team, record.Id, now);
        var written = ResultFromEvaluation("record", parsed, now, after, "write") with
        { Disposition = idempotent ? "idempotent" : "written", Status = after.Status, Cause = after.Cause, Detail = after.Detail, Wrote = wrote, Idempotent = idempotent, ArtifactPath = record.RelativePath, PlannedArtifactPath = record.RelativePath, ContentSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), NormalizedRecord = JsonDocument.Parse(bytes).RootElement.Clone(), TimestampSource = timestampSource, CreatedDirectories = createdDirectories, Expired = after.Status == "expired", RecoveryHint = null };
        if (after.Status != "active") written = written with
        { Disposition = after.Status == "unavailable" ? "unavailable" : "refused", Wrote = wrote, Idempotent = idempotent, RecoveryHint = "The local file state is retained, but complete post-write evaluation did not verify an active record." };
        return Emit(writer, parsed.Format, written);
    }

    private static bool TryValidateInputAncestors(string path, out string error)
    {
        error = "";
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) { error = "input path has no filesystem root"; return false; }
            var current = root;
            var segments = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { error = $"input path component is a symlink: {current}"; return false; }
                if (i < segments.Length - 1 && (attributes & FileAttributes.Directory) == 0)
                { error = $"input path parent is not a directory: {current}"; return false; }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static RulingResult ResultFromEvaluation(string operation, Parsed parsed, DateTimeOffset now, RulingEvaluation evaluation, string mode)
    {
        var target = operation == "record"
            ? evaluation.Status == "conflict" ? evaluation.Records.FirstOrDefault(x => x.Id == parsed.Id) : null
            : evaluation.Status is "active" or "superseded" or "expired" or "conflict"
                ? evaluation.Records.FirstOrDefault(x => x.Id == parsed.Id)
                : null;
        return new()
        {
            Operation = operation, Id = parsed.Id!, Domain = parsed.Domain!, Team = parsed.Team!, Mode = mode,
            Disposition = evaluation.Status switch { "missing" => "refused", "unavailable" => "unavailable", "conflict" => "refused", _ => "shown" },
            Status = evaluation.Status, Cause = evaluation.Cause, Detail = evaluation.Detail,
            AuthorityStatus = "supplied-not-authenticated", PublicationStatus = "not-verified",
            EvaluatedAt = RulingArtifact.FormatTimestamp(now), Wrote = false, Idempotent = false,
            Expired = target is null ? null : target.ExpiresAt is not null && now >= target.ExpiresAt.Value,
            NormalizedRecord = target is null ? null : JsonDocument.Parse(RulingArtifact.Serialize(target)).RootElement.Clone(),
            ContentSha256 = target?.Sha256, ArtifactPath = target?.RelativePath,
            TimestampSource = operation == "record" && target is not null ? "existing" : null,
            SupersededBy = evaluation.SupersededBy,
            ReplacementIds = evaluation.ReplacementIds, Diagnostics = evaluation.Diagnostics,
            RecoveryHint = evaluation.Status is "unavailable" or "conflict" ? "Repair the exact-scope record graph through the responsible host process; do not rewrite history." : evaluation.Status == "missing" ? "Check the exact --domain, --team and ID." : null,
        };
    }

    private static RulingResult Refused(string operation, Parsed p, DateTimeOffset now, string cause, string detail) => new()
    {
        Operation = operation, Id = p.Id ?? "", Domain = p.Domain ?? "", Team = p.Team ?? "", Mode = operation == "record" ? (p.Write ? "write" : "dry-run") : "read-only",
        Disposition = "refused", Status = "unavailable", Cause = cause, Detail = detail,
        AuthorityStatus = "supplied-not-authenticated", PublicationStatus = "not-verified", EvaluatedAt = RulingArtifact.FormatTimestamp(now),
        Wrote = false, Idempotent = false, SupersededBy = [], ReplacementIds = [], Diagnostics = [],
        RecoveryHint = "Correct the supplied identity or record; no existing artifact was changed.",
    };

    private static bool TryParse(string[] args, string operation, out Parsed parsed, out string error)
    {
        parsed = new Parsed(); error = "";
        var allowed = operation == "record"
            ? new HashSet<string>(["--id", "--domain", "--team", "--from-file", "--authority-role", "--format", "--write", "--dry-run"], StringComparer.Ordinal)
            : new HashSet<string>(["--domain", "--team", "--format"], StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var write = false; var dry = false;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) { positional.Add(arg); continue; }
            if (!allowed.Contains(arg)) { error = $"unknown option '{arg}'"; return false; }
            if (arg is "--write" or "--dry-run")
            {
                if (arg == "--write") { if (write) { error = "--write may appear only once"; return false; } write = true; }
                else { if (dry) { error = "--dry-run may appear only once"; return false; } dry = true; }
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) { error = $"{arg} does not take a value"; return false; }
                continue;
            }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i])) { error = $"{arg} requires a nonblank value"; return false; }
            if (!values.TryAdd(arg, args[i])) { error = $"{arg} may appear only once"; return false; }
        }
        if (write && dry) { error = "--write and --dry-run are mutually exclusive"; return false; }
        string? Get(string key) => values.GetValueOrDefault(key);
        var id = operation == "record" ? Get("--id") : positional.Count == 1 ? positional[0] : null;
        if (operation == "record" ? positional.Count != 0 : positional.Count != 1) { error = operation == "record" ? "record accepts no positional values" : "show and validate require exactly one positional ID"; return false; }
        var domain = Get("--domain"); var team = Get("--team");
        var format = Get("--format") ?? "markdown";
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team)) { error = "ID, --domain, and --team are required"; return false; }
        if (format is not ("json" or "markdown")) { error = "--format must be json or markdown"; return false; }
        if (operation == "record" && (Get("--from-file") is null || Get("--authority-role") is null)) { error = "record requires --from-file and --authority-role"; return false; }
        parsed = new Parsed { Id = id, Domain = domain, Team = team, FromFile = Get("--from-file"), AuthorityRole = Get("--authority-role"), Format = format, Write = write };
        return true;
    }

    private static int Emit(TextWriter writer, string format, RulingResult result)
    {
        if (format == "json") writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else
        {
            writer.WriteLine($"# Ruling {result.Operation}: {result.Id}");
            writer.WriteLine();
            writer.WriteLine($"- Scope: `{result.Domain}/{result.Team}`");
            writer.WriteLine($"- Mode: `{result.Mode}`; disposition: **{result.Disposition}**; status: **{result.Status}**");
            writer.WriteLine($"- Cause: `{result.Cause}`");
            writer.WriteLine($"- Authority: `{result.AuthorityStatus}`; publication: `{result.PublicationStatus}`");
            writer.WriteLine($"- Evaluated at: `{result.EvaluatedAt}`");
            writer.WriteLine($"- Wrote: `{result.Wrote}`; idempotent: `{result.Idempotent}`; expired: `{result.Expired?.ToString() ?? "unknown"}`");
            if (result.ArtifactPath is not null) writer.WriteLine($"- Artifact: `{result.ArtifactPath}`");
            if (result.PlannedArtifactPath is not null) writer.WriteLine($"- Planned artifact: `{result.PlannedArtifactPath}`");
            if (result.ContentSha256 is not null) writer.WriteLine($"- SHA-256: `{result.ContentSha256}`");
            writer.WriteLine($"- Detail: {result.Detail}");
            if (result.RecoveryHint is not null) writer.WriteLine($"- Next: {result.RecoveryHint}");
            if (result.Diagnostics.Count > 0)
            {
                writer.WriteLine("- Diagnostics:");
                foreach (var d in result.Diagnostics) writer.WriteLine($"  - `{d.Cause}` {d.Path}: {d.Detail}");
            }
            if (result.NormalizedRecord is not null)
            {
                writer.WriteLine(); writer.WriteLine("```json"); writer.WriteLine(result.NormalizedRecord.Value.GetRawText()); writer.WriteLine("```");
            }
            writer.WriteLine();
            writer.WriteLine("Structured result (same facts as JSON):");
            writer.WriteLine("```json");
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine("```");
        }
        return (result.Status is "active" or "superseded" or "expired")
            && (result.Disposition is "preview" or "written" or "idempotent" or "shown" or "validated") ? 0 : 1;
    }

    private static void WriteHelp(TextWriter writer, string operation)
    {
        writer.WriteLine(operation == "record" ? UsageRecord : UsageRead);
        writer.WriteLine("Local immutable, explicitly scoped record only. Authority is supplied, not authenticated; publication is never inferred.");
    }

    private sealed class Parsed
    {
        public string? Id { get; init; }
        public string? Domain { get; init; }
        public string? Team { get; init; }
        public string? FromFile { get; init; }
        public string? AuthorityRole { get; init; }
        public string Format { get; init; } = "markdown";
        public bool Write { get; init; }
    }

    private sealed record RulingResult
    {
        [JsonPropertyName("operation")] public string Operation { get; init; } = "";
        [JsonPropertyName("id")] public string Id { get; init; } = "";
        [JsonPropertyName("domain")] public string Domain { get; init; } = "";
        [JsonPropertyName("team")] public string Team { get; init; } = "";
        [JsonPropertyName("mode")] public string Mode { get; init; } = "";
        [JsonPropertyName("disposition")] public string Disposition { get; init; } = "";
        [JsonPropertyName("status")] public string Status { get; init; } = "unavailable";
        [JsonPropertyName("cause")] public string Cause { get; init; } = "";
        [JsonPropertyName("detail")] public string Detail { get; init; } = "";
        [JsonPropertyName("authority_status")] public string AuthorityStatus { get; init; } = "supplied-not-authenticated";
        [JsonPropertyName("publication_status")] public string PublicationStatus { get; init; } = "not-verified";
        [JsonPropertyName("timestamp_source")] public string? TimestampSource { get; init; }
        [JsonPropertyName("evaluated_at")] public string EvaluatedAt { get; init; } = "";
        [JsonPropertyName("normalized_record")] public JsonElement? NormalizedRecord { get; init; }
        [JsonPropertyName("content_sha256")] public string? ContentSha256 { get; init; }
        [JsonPropertyName("artifact_path")] public string? ArtifactPath { get; init; }
        [JsonPropertyName("planned_artifact_path")] public string? PlannedArtifactPath { get; init; }
        [JsonPropertyName("wrote")] public bool Wrote { get; init; }
        [JsonPropertyName("idempotent")] public bool Idempotent { get; init; }
        [JsonPropertyName("expired")] public bool? Expired { get; init; }
        [JsonPropertyName("superseded_by")] public IReadOnlyList<string> SupersededBy { get; init; } = [];
        [JsonPropertyName("replacement_ids")] public IReadOnlyList<string> ReplacementIds { get; init; } = [];
        [JsonPropertyName("diagnostics")] public IReadOnlyList<RulingDiagnostic> Diagnostics { get; init; } = [];
        [JsonPropertyName("created_directories")] public IReadOnlyList<string> CreatedDirectories { get; init; } = [];
        [JsonPropertyName("recovery_hint")] public string? RecoveryHint { get; init; }
    }
}
