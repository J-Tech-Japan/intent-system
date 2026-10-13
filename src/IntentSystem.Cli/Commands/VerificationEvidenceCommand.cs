using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>Records and observes supplied local TRX bytes without treating them as execution authority.</summary>
internal static class VerificationEvidenceCommand
{
    private const int MaximumSiblingEntries = VerificationEvidenceStore.MaximumSiblingEntries;
    private const string Usage = "Usage: intent-cli verification evidence record --id <id> ... | show|validate <id>|--id <id> --execution-unit <unit> --domain <domain> --team <team> --repo <owner/repo> --head-sha <40-lowerhex> [record: --packet-sha256 <64-lowerhex> --proof <proof-id> --trx <root-relative-path> [--write|--dry-run]] [--format json|markdown]";
    private const string PublicationGuidance = "Publication is not verified by this command. To make a local record visible in a fresh clone, stage the artifact and commit/plain-push it yourself.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record Output(
        [property: JsonPropertyName("operation")] string Operation,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("execution_unit")] string? ExecutionUnit,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("team")] string? Team,
        [property: JsonPropertyName("target_repo")] string? TargetRepo,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("disposition")] string Disposition,
        [property: JsonPropertyName("wrote")] bool Wrote,
        [property: JsonPropertyName("idempotent")] bool Idempotent,
        [property: JsonPropertyName("head_binding")] string HeadBinding,
        [property: JsonPropertyName("producer")] string Producer,
        [property: JsonPropertyName("execution")] string Execution,
        [property: JsonPropertyName("acceptance")] string Acceptance,
        [property: JsonPropertyName("publication")] string Publication,
        [property: JsonPropertyName("publication_guidance")] string PublicationGuidance,
        [property: JsonPropertyName("packet_path")] string? PacketPath,
        [property: JsonPropertyName("observed_packet_sha256")] string? ObservedPacketSha256,
        [property: JsonPropertyName("planned_artifact_path")] string? PlannedArtifactPath,
        [property: JsonPropertyName("artifact_path")] string? ArtifactPath,
        [property: JsonPropertyName("artifact_sha256")] string? ArtifactSha256,
        [property: JsonPropertyName("trx_sha256")] string? TrxSha256,
        [property: JsonPropertyName("binding")] VerificationEvidenceBinding? Binding,
        [property: JsonPropertyName("report")] TrxEvidenceReader.Summary? Report,
        [property: JsonPropertyName("diagnostics")] IReadOnlyList<VerificationEvidenceDiagnostic> Diagnostics,
        [property: JsonPropertyName("residual_owned_paths")] IReadOnlyList<string> ResidualOwnedPaths);

    private sealed record Options(
        string? Operation, string? Id, string? Unit, string? Domain, string? Team, string? Repo,
        string? HeadSha, string? PacketSha, string? Proof, string? Trx, string? Format,
        bool Write, bool DryRun, string? Error);

    private sealed record PacketContext(
        string RelativePath, string Sha256, string Domain, string Team, string TargetRepo,
        PacketVerificationContract.Result Contract, VerificationEvidenceBinding? Binding);

    private sealed class EventFrame(bool mapping, string path)
    {
        public bool IsMapping { get; } = mapping;
        public string Path { get; } = path;
        public bool ExpectingKey { get; set; } = mapping;
        public string? CurrentKey { get; set; }
        public int NextIndex { get; set; }
    }

    internal static int Execute(CliContext context, string[] args, TextWriter writer) =>
        Execute(context.RepoRoot, args, writer, null);

    internal static int Execute(string repoRoot, string[] args, TextWriter writer,
        Action<string, string>? beforeOperation = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            writer.WriteLine("Local observation only: the supplied TRX is not proof that this CLI executed a command, authenticated its producer, passed an acceptance criterion, or published a record.");
            return 0;
        }
        if (args.Length == 2 && (args[0] is "record" or "show" or "validate") && args[1] == "--help")
        {
            writer.WriteLine(Usage);
            writer.WriteLine("Local observation only: the supplied TRX is not proof that this CLI executed a command, authenticated its producer, passed an acceptance criterion, or published a record.");
            return 0;
        }

        var options = Parse(args);
        if (options.Error is not null)
        {
            if (options.Format is not ("json" or "markdown"))
            {
                writer.WriteLine(options.Error);
                writer.WriteLine(Usage);
                return 1;
            }
            Emit(writer, options.Format, Refusal(options, "invalid", "verification-evidence-argument-invalid",
                options.Error, "refused", [new("verification-evidence-argument-invalid", null, options.Error)]));
            return 1;
        }

        if (options.Operation is null)
        {
            writer.WriteLine(Usage);
            return 1;
        }

        if (!ValidateOptions(options, out var validationCause, out var validationDetail))
        {
            Emit(writer, options.Format!, Refusal(options, validationCause == "verification-evidence-identity-conflict" ? "conflict" : "invalid",
                validationCause, validationDetail, "refused",
                [new(validationCause, null, validationDetail)]));
            return 1;
        }

        try
        {
            var absoluteRoot = Path.GetFullPath(repoRoot);
            if (!TryCheckRoot(absoluteRoot, out var rootDetail))
            {
                Emit(writer, options.Format!, Refusal(options, "unavailable", "verification-evidence-read-unavailable",
                    rootDetail, "refused", [new("verification-evidence-read-unavailable", null, rootDetail)]));
                return 1;
            }

            return options.Operation switch
            {
                "record" => Record(absoluteRoot, options, writer, beforeOperation),
                "show" => ReadStored(absoluteRoot, options, writer, "show", beforeOperation),
                "validate" => ReadStored(absoluteRoot, options, writer, "validate", beforeOperation),
                _ => 1,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var detail = "a bounded local filesystem operation could not be completed";
            Emit(writer, options.Format!, Refusal(options, "unavailable", "verification-evidence-read-unavailable",
                detail, "refused", [new("verification-evidence-read-unavailable", null, detail)]));
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        string? format = null;
        for (var index = 0; index + 1 < args.Length; index++)
            if (args[index] == "--format" && args[index + 1] is "json" or "markdown") { format = args[index + 1]; break; }

        if (args.Length == 0) return new(null, null, null, null, null, null, null, null, null, null, format, false, false, "A verification subcommand is required.");
        var operation = args[0];
        if (operation is not ("record" or "show" or "validate"))
            return new(operation, null, null, null, null, null, null, null, null, null, format, false, false, $"Unknown verification evidence operation '{operation}'.");

        string? id = null, unit = null, domain = null, team = null, repo = null;
        string? headSha = null, packetSha = null, proof = null, trx = null;
        string? suppliedFormat = null, error = null;
        var write = false;
        var dryRun = false;
        var positionalId = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                if ((operation is "show" or "validate") && index == 1 && id is null && !positionalId)
                {
                    id = option;
                    positionalId = true;
                    continue;
                }
                error = "show and validate accept exactly one positional ID and no additional positional arguments.";
                break;
            }
            if (option is "--write" or "--dry-run")
            {
                if (operation != "record") { error = $"'{option}' is only valid for record."; break; }
                if (!seen.Add(option)) { error = $"Duplicate flag '{option}'."; break; }
                if (option == "--write") write = true;
                else dryRun = true;
                continue;
            }
            if (option is not ("--id" or "--execution-unit" or "--domain" or "--team" or "--repo" or "--head-sha" or "--packet-sha256" or "--proof" or "--trx" or "--format"))
            { error = $"Unknown argument '{option}'."; break; }
            if (option == "--id" && positionalId)
            { error = "a positional ID and --id cannot be supplied together."; break; }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            { error = $"Missing value for '{option}'."; break; }
            if (!seen.Add(option)) { error = $"Duplicate argument '{option}'."; break; }
            var value = args[++index];
            switch (option)
            {
                case "--id": id = value; break;
                case "--execution-unit": unit = value; break;
                case "--domain": domain = value; break;
                case "--team": team = value; break;
                case "--repo": repo = value; break;
                case "--head-sha": headSha = value; break;
                case "--packet-sha256": packetSha = value; break;
                case "--proof": proof = value; break;
                case "--trx": trx = value; break;
                case "--format": suppliedFormat = value; break;
            }
        }
        if (write && dryRun) error ??= "--write and --dry-run are mutually exclusive.";
        if (suppliedFormat is not null && suppliedFormat is not ("json" or "markdown")) error ??= "--format must be json or markdown.";
        format = suppliedFormat is "json" or "markdown" ? suppliedFormat : format ?? "json";

        foreach (var required in new[] { ("--id", id), ("--execution-unit", unit), ("--domain", domain), ("--team", team), ("--repo", repo), ("--head-sha", headSha) })
            if (required.Item2 is null) error ??= $"{required.Item1} is required.";
        if (operation == "record")
        {
            foreach (var required in new[] { ("--packet-sha256", packetSha), ("--proof", proof), ("--trx", trx) })
                if (required.Item2 is null) error ??= $"{required.Item1} is required for record.";
        }
        else if (packetSha is not null || proof is not null || trx is not null || write || dryRun)
            error ??= "show and validate accept no TRX, proof, packet digest, write, or dry-run arguments.";

        return new(operation, id, unit, domain, team, repo, headSha, packetSha, proof, trx, format,
            write, dryRun, error);
    }

    private static bool ValidateOptions(Options options, out string cause, out string detail)
    {
        cause = "verification-evidence-identity-conflict";
        detail = "one or more supplied identity values are invalid";
        if (!RulingArtifact.TryIdentifier(options.Id, out _)
            || !RulingArtifact.TryIdentifier(options.Unit, out _)
            || !RulingArtifact.TryIdentifier(options.Domain, out _)
            || !RulingArtifact.TryIdentifier(options.Team, out _)
            || !ValidRepo(options.Repo)) return false;
        if (!LowerHex(options.HeadSha, 40))
        {
            cause = "verification-evidence-argument-invalid";
            detail = "--head-sha must be exactly 40 lowercase hexadecimal characters";
            return false;
        }
        if (options.Operation == "record")
        {
            if (!LowerHex(options.PacketSha, 64))
            {
                cause = "verification-evidence-argument-invalid";
                detail = "--packet-sha256 must be exactly 64 lowercase hexadecimal characters";
                return false;
            }
            if (!ValidProofId(options.Proof))
            {
                cause = "verification-evidence-argument-invalid";
                detail = "--proof must be a canonical G865 proof identifier";
                return false;
            }
            if (string.IsNullOrWhiteSpace(options.Trx) || Path.IsPathRooted(options.Trx)
                || options.Trx.Contains(':', StringComparison.Ordinal))
            {
                cause = "verification-evidence-argument-invalid";
                detail = "--trx must be a nonempty root-relative path";
                return false;
            }
        }
        cause = string.Empty;
        detail = string.Empty;
        return true;
    }

    private static int Record(string root, Options options, TextWriter writer, Action<string, string>? beforeOperation)
    {
        if (!TryLoadPacket(root, options, beforeOperation, out var packet, out var packetFailure))
        {
            Emit(writer, options.Format!, packetFailure!);
            return 1;
        }
        if (!string.Equals(packet!.Sha256, options.PacketSha, StringComparison.Ordinal))
        {
            const string detail = "packet.yaml bytes do not match --packet-sha256";
            Emit(writer, options.Format!, FromPacket(options, packet, "stale", "verification-evidence-packet-changed",
                detail, "refused", [new("verification-evidence-packet-changed", packet.RelativePath, detail)]));
            return 1;
        }
        if (packet.Binding is null)
        {
            var detail = $"proof '{options.Proof}' is missing or is not local-required";
            Emit(writer, options.Format!, FromPacket(options, packet, "invalid", "verification-evidence-proof-unsupported",
                detail, "refused", [new("verification-evidence-proof-unsupported", packet.RelativePath, detail)]));
            return 1;
        }

        if (!TryResolveContainedFile(root, options.Trx!, out var trxPath, out var trxRelative, out var pathCause, out var pathDetail))
        {
            Emit(writer, options.Format!, FromPacket(options, packet, "unavailable", pathCause, pathDetail,
                "refused", [new(pathCause, trxRelative, pathDetail)]));
            return 1;
        }
        beforeOperation?.Invoke("trx-read", trxPath);
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(trxPath, TrxEvidenceReader.MaximumBytes,
                out var trxBytes, out var trxFailure, out _))
        {
            var cause = trxFailure == CrossRuntimeReviewFileReadFailure.TooLarge
                ? "verification-evidence-resource-limit" : "verification-evidence-read-unavailable";
            var detail = trxFailure == CrossRuntimeReviewFileReadFailure.TooLarge
                ? "TRX input exceeds the 16 MiB limit" : $"TRX input could not be read: {trxFailure}";
            Emit(writer, options.Format!, FromPacket(options, packet, "unavailable", cause, detail,
                "refused", [new(cause, trxRelative, detail)]));
            return 1;
        }

        var trxRead = TrxEvidenceReader.Read(trxBytes);
        if (!trxRead.Success || trxRead.Report is null)
        {
            Emit(writer, options.Format!, FromPacket(options, packet, "invalid", trxRead.Cause, trxRead.Detail,
                "refused", [new(trxRead.Cause, trxRelative, trxRead.Detail)],
                trxSha: Hash(trxBytes)));
            return 1;
        }

        var trxSha = Hash(trxBytes);
        var candidate = new VerificationEvidenceRecord("1", options.Id!, options.Domain!, options.Team!, options.Unit!,
            packet.TargetRepo, options.HeadSha!, packet.Sha256, packet.Binding, trxSha,
            Convert.ToBase64String(trxBytes), trxRead.Report);
        var store = new VerificationEvidenceStore(root, beforeOperation);
        var targetPath = store.RelativePath(options.Domain!, options.Team!, options.Unit!, options.Id!);
        var bytes = VerificationEvidenceStore.Serialize(candidate);
        if (bytes.Length > VerificationEvidenceStore.MaximumArtifactBytes)
        {
            Emit(writer, options.Format!, FromPacket(options, packet, "unavailable", "verification-evidence-resource-limit",
                "canonical record exceeds the 24 MiB stored-artifact limit", "refused",
                [new("verification-evidence-resource-limit", targetPath, "serialized record is too large")],
                trxSha: trxSha, binding: packet.Binding, report: trxRead.Report));
            return 1;
        }

        var existing = store.Read(options.Domain!, options.Team!, options.Unit!, options.Id!);
        if (existing.State != "missing")
        {
            if (existing.State == "current" && existing.Bytes is not null
                && existing.Bytes.AsSpan().SequenceEqual(bytes) && existing.Record is not null)
            {
                Emit(writer, options.Format!, FromPacket(options, packet, "current", "verification-evidence-current",
                    "identical canonical evidence record already exists", options.Write ? "idempotent" : "idempotent",
                    [], artifactPath: existing.Path, artifactSha: existing.Sha256,
                    trxSha: existing.Record.TrxSha256, binding: existing.Record.Binding, report: existing.Record.Report,
                    idempotent: true));
                return 0;
            }
            var verifiedExisting = existing.State == "current";
            var malformedExistingTarget = existing.State == "unavailable"
                && existing.Cause == "verification-evidence-record-invalid";
            var conflict = verifiedExisting || existing.State == "conflict" || malformedExistingTarget;
            var existingCause = existing.State == "conflict"
                ? existing.Cause
                : malformedExistingTarget ? "verification-evidence-record-conflict"
                : verifiedExisting ? "verification-evidence-record-conflict" : existing.Cause;
            Emit(writer, options.Format!, FromPacket(options, packet,
                conflict ? "conflict" : "unavailable",
                existingCause,
                conflict ? "target already exists with different, malformed, or mismatched content" : existing.Detail,
                "refused", malformedExistingTarget
                    ? [new("verification-evidence-record-conflict", existing.Path, "existing target is not a canonical record")]
                    : existing.Diagnostics,
                artifactPath: verifiedExisting ? existing.Path : null,
                artifactSha: verifiedExisting ? existing.Sha256 : null,
                trxSha: verifiedExisting ? existing.Record?.TrxSha256 : null,
                binding: verifiedExisting ? existing.Record?.Binding : null,
                report: verifiedExisting ? existing.Record?.Report : null));
            return 1;
        }

        if (!options.Write)
        {
            Emit(writer, options.Format!, FromPacket(options, packet, "current", "verification-evidence-current",
                "validated supplied TRX and current packet binding; preview created no state",
                "planned", [], plannedPath: targetPath, trxSha: trxSha, binding: packet.Binding, report: trxRead.Report));
            return 0;
        }

        var write = store.Write(candidate, bytes, () => Recheck(root, options, packet, beforeOperation));
        if (write.Success)
        {
            var replay = write.Idempotent;
            Emit(writer, options.Format!, FromPacket(options, packet, "current", "verification-evidence-current",
                write.Detail, replay ? "idempotent" : "recorded", write.Diagnostics,
                artifactPath: write.Path, artifactSha: write.Sha256, trxSha: trxSha,
                binding: candidate.Binding, report: candidate.Report,
                wrote: write.Wrote, idempotent: write.Idempotent, residual: write.ResidualOwnedPaths));
            return 0;
        }

        var status = write.State is "conflict" or "invalid" ? write.State : write.State == "stale" ? "stale" : "unavailable";
        VerificationEvidenceRecord? known = null;
        if (write.Path is not null && write.Sha256 is not null)
        {
            var stored = store.Read(options.Domain!, options.Team!, options.Unit!, options.Id!);
            if (stored.State == "current" && stored.Record is not null
                && string.Equals(stored.Sha256, write.Sha256, StringComparison.Ordinal)) known = stored.Record;
        }
        Emit(writer, options.Format!, FromPacket(options, packet, status, write.Cause, write.Detail, "refused",
            write.Diagnostics, artifactPath: known is null ? null : write.Path,
            artifactSha: known is null ? null : write.Sha256,
            trxSha: known?.TrxSha256 ?? trxSha, binding: known?.Binding ?? candidate.Binding,
            report: known?.Report ?? candidate.Report, wrote: write.Wrote, idempotent: false,
            residual: write.ResidualOwnedPaths));
        return 1;
    }

    private static int ReadStored(string root, Options options, TextWriter writer, string operation,
        Action<string, string>? beforeOperation)
    {
        var store = new VerificationEvidenceStore(root, beforeOperation);
        var stored = store.Read(options.Domain!, options.Team!, options.Unit!, options.Id!);
        if (stored.State != "current" || stored.Record is null || stored.Path is null || stored.Sha256 is null)
        {
            var state = stored.State is "missing" or "conflict" ? stored.State : "unavailable";
            Emit(writer, options.Format!, Refusal(options, state, stored.Cause, stored.Detail, "refused", stored.Diagnostics,
                plannedPath: null));
            return 1;
        }

        var record = stored.Record;
        var verified = FromStored(options, operation, record, stored.Path!, stored.Sha256!);
        if (!string.Equals(options.Repo, record.TargetRepo, StringComparison.Ordinal))
        {
            const string detail = "requested repository does not match the stored record target_repo";
            Emit(writer, options.Format!, verified with
            {
                State = "conflict", Cause = "verification-evidence-identity-conflict", Detail = detail,
                Disposition = "refused", PacketPath = null, ObservedPacketSha256 = null,
                ArtifactPath = null, ArtifactSha256 = null, TrxSha256 = null, Binding = null, Report = null,
                Diagnostics = [new("verification-evidence-identity-conflict", null, detail)],
            });
            return 1;
        }
        var currentOptions = options with { Proof = record.Binding.ProofId };
        if (!TryLoadPacket(root, currentOptions, beforeOperation, out var current, out var packetFailure))
        {
            var failure = packetFailure!;
            Emit(writer, options.Format!, verified with
            {
                State = failure.State == "invalid" ? "unavailable" : failure.State,
                Cause = failure.Cause,
                Detail = failure.Detail,
                Disposition = "refused",
                PacketPath = failure.PacketPath,
                ObservedPacketSha256 = failure.ObservedPacketSha256,
                Diagnostics = failure.Diagnostics,
            });
            return 1;
        }

        if (current!.Domain != record.Domain || current.Team != record.Team || current.TargetRepo != record.TargetRepo)
        {
            var detail = "current packet identity does not match the stored record scope";
            Emit(writer, options.Format!, verified with
            {
                State = "conflict", Cause = "verification-evidence-identity-conflict", Detail = detail,
                Disposition = "refused", PacketPath = current.RelativePath, ObservedPacketSha256 = current.Sha256,
                Diagnostics = [new("verification-evidence-identity-conflict", current.RelativePath, detail)],
            });
            return 1;
        }

        if (options.HeadSha != record.ChildHeadSha)
        {
            var detail = "supplied child head differs from the stored caller-supplied binding";
            Emit(writer, options.Format!, verified with
            {
                State = "stale", Cause = "verification-evidence-stale-head", Detail = detail,
                Disposition = operation == "show" ? "shown" : "refused",
                PacketPath = current.RelativePath, ObservedPacketSha256 = current.Sha256,
                Diagnostics = [new("verification-evidence-stale-head", current.RelativePath, detail)],
            });
            return operation == "show" ? 0 : 1;
        }

        if (current.Binding is null)
        {
            var detail = $"stored proof '{record.Binding.ProofId}' is missing or no longer local-required";
            Emit(writer, options.Format!, verified with
            {
                State = "stale", Cause = "verification-evidence-stale-packet", Detail = detail,
                Disposition = operation == "show" ? "shown" : "refused",
                PacketPath = current.RelativePath, ObservedPacketSha256 = current.Sha256,
                Diagnostics = [new("verification-evidence-proof-unsupported", current.RelativePath, detail)],
            });
            return operation == "show" ? 0 : 1;
        }

        if (current.Sha256 != record.PacketSha256 || !BindingsEqual(current.Binding, record.Binding))
        {
            var detail = current.Sha256 != record.PacketSha256
                ? "current packet bytes differ from the stored binding"
                : "current selected proof binding differs from the stored record";
            Emit(writer, options.Format!, verified with
            {
                State = "stale", Cause = "verification-evidence-stale-packet", Detail = detail,
                Disposition = operation == "show" ? "shown" : "refused",
                PacketPath = current.RelativePath, ObservedPacketSha256 = current.Sha256,
                Diagnostics = [new("verification-evidence-stale-packet", current.RelativePath, detail)],
            });
            return operation == "show" ? 0 : 1;
        }

        Emit(writer, options.Format!, verified with
        {
            State = "current", Cause = "verification-evidence-current",
            Detail = operation == "show" ? "verified stored record matches current packet context" : "stored record matches current packet context",
            Disposition = operation == "show" ? "shown" : "current",
            PacketPath = current.RelativePath, ObservedPacketSha256 = current.Sha256,
            Diagnostics = [],
        });
        return 0;
    }

    private static bool TryLoadPacket(string root, Options options, Action<string, string>? beforeOperation,
        out PacketContext? packet, out Output? failure)
    {
        packet = null;
        failure = null;
        var relative = $".intent-cli/issues/{options.Unit}/packet.yaml";
        var directories = new[] { ".intent-cli", "issues", options.Unit! };
        if (!TryCheckDirectoryChain(root, directories, out var pathCause, out var pathDetail))
        {
            failure = Refusal(options, "unavailable", pathCause, pathDetail, "refused",
                [new(pathCause, relative, pathDetail)], packetPath: relative);
            return false;
        }
        var full = Path.Combine(root, ".intent-cli", "issues", options.Unit!, "packet.yaml");
        beforeOperation?.Invoke("packet-read", full);
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(full, PacketVerificationContract.MaximumBytes,
                out var bytes, out var readFailure, out _))
        {
            var cause = readFailure == CrossRuntimeReviewFileReadFailure.TooLarge
                ? "verification-evidence-resource-limit" : "verification-evidence-read-unavailable";
            var detail = readFailure == CrossRuntimeReviewFileReadFailure.TooLarge
                ? "packet.yaml exceeds the 1 MiB G865 limit" : $"packet.yaml could not be read ({readFailure})";
            failure = Refusal(options, "unavailable", cause, detail, "refused",
                [new(cause, relative, detail)], packetPath: relative);
            return false;
        }
        var sha = Hash(bytes);
        var contract = PacketVerificationContract.Evaluate(options.Unit!, relative, bytes);
        if (contract.State is not ("complete" or "incomplete"))
        {
            var diagnostic = contract.Diagnostics.FirstOrDefault();
            var identityIssue = diagnostic?.Detail.Contains("source_execution_unit", StringComparison.Ordinal) == true;
            var cause = identityIssue ? "verification-evidence-identity-conflict" : "verification-evidence-packet-invalid";
            var state = identityIssue ? "conflict" : contract.State == "unavailable" ? "unavailable" : "invalid";
            const string contractDetail = "packet.yaml does not satisfy the G865 structural source-contract requirements";
            failure = Refusal(options, state, cause, contractDetail, "refused",
                [new(cause, relative, contractDetail)], packetPath: relative, packetSha: sha);
            return false;
        }
        if (!TryReadIdentity(bytes, out var domain, out var team, out var targetRepo, out var identityDetail))
        {
            failure = Refusal(options, "conflict", "verification-evidence-identity-conflict", identityDetail,
                "refused", [new("verification-evidence-identity-conflict", relative, identityDetail)],
                packetPath: relative, packetSha: sha);
            return false;
        }
        if (domain != options.Domain || team != options.Team || targetRepo != options.Repo)
        {
            var detail = "packet domain, team, or target_repo does not exactly match the requested context";
            failure = Refusal(options, "conflict", "verification-evidence-identity-conflict", detail,
                "refused", [new("verification-evidence-identity-conflict", relative, detail)],
                packetPath: relative, packetSha: sha);
            return false;
        }

        var selectedProof = options.Proof is null ? null : contract.Proofs.FirstOrDefault(proof => proof.Id == options.Proof);
        VerificationEvidenceBinding? binding = null;
        if (selectedProof is not null && selectedProof.Evidence == "local-required")
        {
            var criteria = selectedProof.AcceptanceCriteria
                .Select(id => contract.AcceptanceCriteria.First(criterion => criterion.Id == id))
                .OrderBy(criterion => criterion.Id, StringComparer.Ordinal)
                .Select(criterion => new VerificationEvidenceCriterion(criterion.Id, criterion.CriterionIndex, criterion.Text))
                .ToArray();
            var lane = contract.Lanes.First(item => item.Id == selectedProof.Lane);
            binding = new(selectedProof.Id, criteria, lane.Id, lane.Weight, lane.Affected,
                selectedProof.Command, selectedProof.Evidence);
        }
        packet = new(relative, sha, domain, team, targetRepo, contract, binding);
        return true;
    }

    private static bool TryReadIdentity(byte[] bytes, out string domain, out string team, out string targetRepo, out string detail)
    {
        domain = team = targetRepo = string.Empty;
        detail = "packet identity is missing, aliased, tagged, or malformed";
        string yaml;
        try { yaml = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return false; }
        if (yaml.StartsWith('\uFEFF')) yaml = yaml[1..];

        if (!TryScanIdentityTagsAndAliases(yaml, out var invalidIdentityNodes) || invalidIdentityNodes) return false;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root) return false;
            var identities = root.Children.Where(pair => KeyText(pair.Key) == "implementation_issue_packet").ToArray();
            if (identities.Length != 1 || identities[0].Value is not YamlMappingNode identity) return false;
            if (!TryIdentityText(identity, "domain", out domain)
                || !TryIdentityText(identity, "team", out team)
                || !TryIdentityText(identity, "target_repo", out targetRepo)) return false;
            if (!RulingArtifact.TryIdentifier(domain, out _)
                || !RulingArtifact.TryIdentifier(team, out _)
                || !ValidRepo(targetRepo)) return false;
            detail = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryIdentityText(YamlMappingNode identity, string key, out string value)
    {
        value = string.Empty;
        var fields = identity.Children.Where(pair => KeyText(pair.Key) == key).ToArray();
        if (fields.Length != 1 || fields[0].Key is not YamlScalarNode keyNode
            || fields[0].Value is not YamlScalarNode scalar
            || IsTagged(keyNode) || IsTagged(scalar) || IsPlainNull(scalar) || scalar.Value is null
            || string.IsNullOrWhiteSpace(scalar.Value)) return false;
        value = scalar.Value;
        return true;
    }

    private static bool TryScanIdentityTagsAndAliases(string yaml, out bool invalid)
    {
        invalid = false;
        var stack = new Stack<EventFrame>();
        var scalarAnchors = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var parser = new Parser(new StringReader(yaml));
            while (parser.MoveNext())
            {
                switch (parser.Current)
                {
                    case MappingStart mapping:
                    {
                        var path = Consume(stack, null);
                        if (!mapping.IsImplicit && IsIdentityPath(path)) invalid = true;
                        stack.Push(new(true, path));
                        break;
                    }
                    case SequenceStart sequence:
                    {
                        var path = Consume(stack, null);
                        if (!sequence.IsImplicit && IsIdentityPath(path)) invalid = true;
                        stack.Push(new(false, path));
                        break;
                    }
                    case Scalar scalar:
                    {
                        var keyPosition = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        var path = keyPosition && scalar.Value is not null
                            ? stack.Peek().Path + "/" + EscapePath(scalar.Value)
                            : CurrentPath(stack);
                        var explicitTag = scalar.Tag.ToString() != "?" || (!scalar.IsPlainImplicit && !scalar.IsQuotedImplicit);
                        if (explicitTag && IsIdentityPath(path)) invalid = true;
                        if (!string.IsNullOrEmpty(scalar.Anchor.ToString())) scalarAnchors[scalar.Anchor.ToString()] = scalar.Value ?? string.Empty;
                        Consume(stack, keyPosition ? scalar.Value : null);
                        break;
                    }
                    case AnchorAlias alias:
                    {
                        var keyPosition = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        var aliasPath = keyPosition && scalarAnchors.TryGetValue(alias.Value.ToString(), out var aliasedKey)
                            ? stack.Peek().Path + "/" + EscapePath(aliasedKey)
                            : CurrentPath(stack);
                        if (IsIdentityPath(aliasPath)) invalid = true;
                        Consume(stack, null);
                        break;
                    }
                    case MappingEnd:
                    case SequenceEnd:
                        if (stack.Count > 0) stack.Pop();
                        break;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static string Consume(Stack<EventFrame> stack, string? scalarKey)
    {
        var path = CurrentPath(stack);
        if (stack.Count == 0) return path;
        var frame = stack.Peek();
        if (frame.IsMapping)
        {
            if (frame.ExpectingKey)
            {
                frame.CurrentKey = scalarKey;
                frame.ExpectingKey = false;
            }
            else
            {
                frame.CurrentKey = null;
                frame.ExpectingKey = true;
            }
        }
        else frame.NextIndex++;
        return path;
    }

    private static string CurrentPath(Stack<EventFrame> stack)
    {
        if (stack.Count == 0) return string.Empty;
        var frame = stack.Peek();
        if (frame.IsMapping)
            return frame.ExpectingKey ? frame.Path + "/<key>" : frame.Path + "/" + EscapePath(frame.CurrentKey ?? "<value>");
        return frame.Path + "/" + frame.NextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string EscapePath(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool IsIdentityPath(string path) => path is "/implementation_issue_packet"
        or "/implementation_issue_packet/domain" or "/implementation_issue_packet/team"
        or "/implementation_issue_packet/target_repo" or "/implementation_issue_packet/source_execution_unit";

    private static string? KeyText(YamlNode node) => node is YamlScalarNode { Value: not null } scalar ? scalar.Value : null;
    private static bool IsTagged(YamlNode node) => node.Tag.ToString() is not ("?" or "!");
    private static bool IsPlainNull(YamlScalarNode scalar) => scalar.Style == ScalarStyle.Plain
        && (string.IsNullOrEmpty(scalar.Value) || scalar.Value is "~" or "null" or "Null" or "NULL");

    private static (bool Matches, string Cause, string Detail) Recheck(string root, Options options,
        PacketContext initial, Action<string, string>? beforeOperation)
    {
        if (!TryLoadPacket(root, options, beforeOperation, out var current, out var failure))
            return (false, failure!.Cause, failure.Detail);
        if (current!.Domain != initial.Domain || current.Team != initial.Team || current.TargetRepo != initial.TargetRepo
            || current.Sha256 != initial.Sha256 || !BindingsEqual(current.Binding, initial.Binding))
            return (false, "verification-evidence-packet-changed", "current packet bytes, identity, or selected proof binding changed before publication");
        return (true, string.Empty, string.Empty);
    }

    private static bool TryResolveContainedFile(string root, string supplied, out string fullFile,
        out string relative, out string cause, out string detail)
    {
        fullFile = string.Empty;
        relative = string.Empty;
        cause = "verification-evidence-read-unavailable";
        detail = "TRX path could not be resolved safely";
        var normalized = supplied.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            cause = "verification-evidence-argument-invalid";
            detail = "--trx must contain only nonempty root-relative path segments";
            return false;
        }
        relative = string.Join('/', segments);
        if (!TryCheckDirectoryChain(root, segments[..^1], out cause, out detail)) return false;
        var parent = segments.Length == 1 ? root : Path.Combine(root, Path.Combine(segments[..^1]));
        if (!TryCheckSiblingName(parent, segments[^1], out var exists, out cause, out detail)) return false;
        var leaf = Path.Combine(parent, segments[^1]);
        if (!exists || !CrossRuntimeReviewFileMode.IsRegularFile(leaf))
        {
            detail = exists ? "TRX target is a link, directory, empty, FIFO, or non-regular file" : "TRX target is missing";
            return false;
        }
        fullFile = leaf;
        return true;
    }

    private static bool TryCheckRoot(string root, out string detail)
    {
        detail = "repository root is not a safe readable directory";
        if (!TryInspectDirectory(root, out var state, out detail) || state != EntryState.Directory)
        {
            if (state == EntryState.Link) detail = "repository root is a link or reparse point";
            return false;
        }
        return TryEnumerateBounded(root, out detail);
    }

    private static bool TryCheckDirectoryChain(string root, IReadOnlyList<string> segments, out string cause, out string detail)
    {
        cause = "verification-evidence-read-unavailable";
        if (!TryCheckRoot(root, out detail)) return false;
        var current = root;
        foreach (var segment in segments)
        {
            if (!TryCheckSiblingName(current, segment, out var exists, out cause, out detail)) return false;
            if (!exists)
            {
                detail = $"required directory '{segment}' is missing";
                cause = "verification-evidence-read-unavailable";
                return false;
            }
            current = Path.Combine(current, segment);
            if (!TryInspectDirectory(current, out var state, out detail))
            {
                cause = state == EntryState.Link ? "verification-evidence-identity-conflict" : "verification-evidence-read-unavailable";
                return false;
            }
            if (state != EntryState.Directory)
            {
                cause = state is EntryState.Link or EntryState.File ? "verification-evidence-identity-conflict" : "verification-evidence-read-unavailable";
                detail = state == EntryState.Link ? "link refused in evidence path" : "path segment is not an existing directory";
                return false;
            }
        }
        return true;
    }

    private static bool TryCheckSiblingName(string parent, string desired, out bool exists, out string cause, out string detail)
    {
        exists = false;
        cause = "verification-evidence-read-unavailable";
        detail = "parent directory cannot be inspected";
        if (!TryInspectDirectory(parent, out var state, out detail) || state != EntryState.Directory)
        {
            cause = state == EntryState.Link ? "verification-evidence-identity-conflict" : "verification-evidence-read-unavailable";
            return false;
        }
        try
        {
            var count = 0;
            foreach (var item in Directory.EnumerateFileSystemEntries(parent))
            {
                if (++count > MaximumSiblingEntries)
                {
                    cause = "verification-evidence-resource-limit";
                    detail = $"parent exceeds the bounded {MaximumSiblingEntries}-entry inspection limit";
                    return false;
                }
                var name = Path.GetFileName(item);
                if (string.Equals(name, desired, StringComparison.Ordinal)) exists = true;
                else if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
                {
                    cause = "verification-evidence-identity-conflict";
                    detail = "a case-alias path segment conflicts with the requested identity";
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            detail = "parent directory cannot be safely enumerated";
            return false;
        }
    }

    private enum EntryState { Missing, File, Directory, Link }

    private static bool TryInspectDirectory(string path, out EntryState state, out string detail)
    {
        if (!TryInspect(path, out state, out detail)) return false;
        if (state != EntryState.Directory) return true;
        return TryEnumerateBounded(path, out detail);
    }

    private static bool TryInspect(string path, out EntryState state, out string detail)
    {
        state = EntryState.Missing;
        detail = string.Empty;
        try
        {
            var file = new FileInfo(path);
            var directory = new DirectoryInfo(path);
            if (file.LinkTarget is not null || directory.LinkTarget is not null)
            { state = EntryState.Link; return true; }
            var attributes = File.GetAttributes(path);
            state = (attributes & FileAttributes.ReparsePoint) != 0 ? EntryState.Link
                : (attributes & FileAttributes.Directory) != 0 ? EntryState.Directory : EntryState.File;
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { state = EntryState.Missing; return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { detail = "filesystem entry cannot be safely inspected"; return false; }
    }

    private static bool TryEnumerateBounded(string directory, out string detail)
    {
        detail = string.Empty;
        try
        {
            var count = 0;
            foreach (var _ in Directory.EnumerateFileSystemEntries(directory))
                if (++count > MaximumSiblingEntries)
                {
                    detail = $"directory exceeds the bounded {MaximumSiblingEntries}-entry inspection limit";
                    return false;
                }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { detail = "directory is not readable for bounded inspection"; return false; }
    }

    private static bool ValidRepo(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part is not ("." or "..")
            && part.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '_' or '.' or '-'));
    }

    private static bool ValidProofId(string? value) => value is not null && value.Length is >= 1 and <= 64
        && value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        && value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
            or >= '0' and <= '9' or '_' or '-');

    private static bool LowerHex(string? value, int length) => value is not null && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool BindingsEqual(VerificationEvidenceBinding? left, VerificationEvidenceBinding? right) =>
        left is null ? right is null : right is not null
        && left.ProofId == right.ProofId && left.LaneId == right.LaneId && left.LaneWeight == right.LaneWeight
        && left.LaneAffected == right.LaneAffected && left.Command == right.Command && left.Evidence == right.Evidence
        && left.AcceptanceCriteria.SequenceEqual(right.AcceptanceCriteria);

    private static Output FromPacket(Options options, PacketContext packet, string state, string cause,
        string detail, string disposition, IReadOnlyList<VerificationEvidenceDiagnostic> diagnostics,
        string? plannedPath = null, string? artifactPath = null, string? artifactSha = null,
        string? trxSha = null, VerificationEvidenceBinding? binding = null, TrxEvidenceReader.Summary? report = null,
        bool wrote = false, bool idempotent = false, IReadOnlyList<string>? residual = null) =>
        new(options.Operation!, options.Id, options.Unit, options.Domain, options.Team, options.Repo,
            state, cause, detail, disposition, wrote, idempotent, "caller-supplied-not-authenticated",
            "not-authenticated", "not-observed", "not-assessed", "not-verified", PublicationGuidance, packet.RelativePath,
            packet.Sha256, plannedPath, artifactPath, artifactSha, trxSha,
            binding ?? packet.Binding, report, diagnostics, residual ?? []);

    private static Output FromStored(Options options, string operation, VerificationEvidenceRecord record,
        string path, string sha) =>
        new(operation, options.Id, options.Unit, options.Domain, options.Team, options.Repo,
            "current", "verification-evidence-current", "verified stored record is being compared with current context",
            operation == "show" ? "shown" : "current", false, operation == "record", "caller-supplied-not-authenticated",
            "not-authenticated", "not-observed", "not-assessed", "not-verified", PublicationGuidance, null, null, null,
            path, sha, record.TrxSha256, record.Binding, record.Report, [], []);

    private static Output Refusal(Options options, string state, string cause, string detail, string disposition,
        IReadOnlyList<VerificationEvidenceDiagnostic> diagnostics, string? packetPath = null, string? packetSha = null,
        string? plannedPath = null, string? artifactPath = null, string? artifactSha = null, string? trxSha = null,
        VerificationEvidenceBinding? binding = null, TrxEvidenceReader.Summary? report = null,
        bool wrote = false, bool idempotent = false, IReadOnlyList<string>? residual = null) =>
        new(options.Operation ?? "evidence", options.Id, options.Unit, options.Domain, options.Team, options.Repo,
            state, cause, detail, disposition, wrote, idempotent, "caller-supplied-not-authenticated",
            "not-authenticated", "not-observed", "not-assessed", "not-verified", PublicationGuidance, packetPath, packetSha,
            plannedPath, artifactPath, artifactSha, trxSha, binding, report, diagnostics, residual ?? []);

    private static void Emit(TextWriter writer, string format, Output output)
    {
        var publicOutput = output with
        {
            Diagnostics = output.Diagnostics.Select(diagnostic => diagnostic with
            {
                Cause = diagnostic.Cause.StartsWith("verification-evidence-", StringComparison.Ordinal)
                    ? diagnostic.Cause
                    : $"verification-evidence-{diagnostic.Cause}",
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(publicOutput, JsonOptions);
        if (format == "json") { writer.WriteLine(json); return; }
        writer.WriteLine("# Local verification evidence");
        writer.WriteLine();
        writer.WriteLine("- Operation:");
        WriteFence(writer, "text", publicOutput.Operation);
        writer.WriteLine($"- State: {publicOutput.State}");
        writer.WriteLine($"- Cause: {publicOutput.Cause}");
        writer.WriteLine("- Detail:");
        WriteFence(writer, "text", publicOutput.Detail);
        writer.WriteLine("- Producer: not authenticated");
        writer.WriteLine("- Execution: not observed");
        writer.WriteLine("- Acceptance: not assessed");
        writer.WriteLine("- Publication: not verified");
        writer.WriteLine("- Publication guidance:");
        WriteFence(writer, "text", publicOutput.PublicationGuidance);
        writer.WriteLine();
        writer.WriteLine("## Structured result (same facts as JSON)");
        writer.WriteLine();
        WriteFence(writer, "json", json);
    }

    private static void WriteFence(TextWriter writer, string language, string value)
    {
        var longest = 0; var current = 0;
        foreach (var character in value)
        {
            if (character == '`') { current++; longest = Math.Max(longest, current); }
            else current = 0;
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        writer.WriteLine(fence + language); writer.WriteLine(value); writer.WriteLine(fence);
    }
}
