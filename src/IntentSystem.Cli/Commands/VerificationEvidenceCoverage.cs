using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>Read-only coverage for caller-selected local G866 records against one G865 packet snapshot.</summary>
internal static class VerificationEvidenceCoverage
{
    internal const int MaximumReferences = 16;
    private const string Usage = "Usage: intent-cli verification evidence coverage --execution-unit <unit> --domain <domain> --team <team> --repo <owner/repo> --head-sha <40-lowerhex> --packet-sha256 <64-lowerhex> [--evidence <proof-id>=<record-id>]... [--format json|markdown]";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record Diagnostic(
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("detail")] string Detail);

    internal sealed record ProofRow(
        [property: JsonPropertyName("proof_id")] string ProofId,
        [property: JsonPropertyName("acceptance_criteria")] IReadOnlyList<VerificationEvidenceCriterion> AcceptanceCriteria,
        [property: JsonPropertyName("lane_id")] string LaneId,
        [property: JsonPropertyName("lane_weight")] string LaneWeight,
        [property: JsonPropertyName("lane_affected")] bool LaneAffected,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("evidence")] string Evidence,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("record_id")] string? RecordId,
        [property: JsonPropertyName("binding_state")] string BindingState,
        [property: JsonPropertyName("artifact_path")] string? ArtifactPath,
        [property: JsonPropertyName("artifact_sha256")] string? ArtifactSha256,
        [property: JsonPropertyName("trx_sha256")] string? TrxSha256,
        [property: JsonPropertyName("report")] TrxEvidenceReader.Summary? Report,
        [property: JsonPropertyName("diagnostics")] IReadOnlyList<Diagnostic> Diagnostics);

    internal sealed record Report(
        [property: JsonPropertyName("execution_unit")] string? ExecutionUnit,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("team")] string? Team,
        [property: JsonPropertyName("target_repo")] string? TargetRepo,
        [property: JsonPropertyName("head_sha")] string? HeadSha,
        [property: JsonPropertyName("requested_packet_sha256")] string? RequestedPacketSha256,
        [property: JsonPropertyName("observed_packet_sha256")] string? ObservedPacketSha256,
        [property: JsonPropertyName("packet_path")] string? PacketPath,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("execution")] string Execution,
        [property: JsonPropertyName("producer_identity")] string ProducerIdentity,
        [property: JsonPropertyName("head_binding")] string HeadBinding,
        [property: JsonPropertyName("acceptance")] string Acceptance,
        [property: JsonPropertyName("publication")] string Publication,
        [property: JsonPropertyName("structural_state")] string? StructuralState,
        [property: JsonPropertyName("missing_acceptance_criteria")] IReadOnlyList<string> MissingAcceptanceCriteria,
        [property: JsonPropertyName("missing_lanes")] IReadOnlyList<string> MissingLanes,
        [property: JsonPropertyName("missing_local_required_lanes")] IReadOnlyList<string> MissingLocalRequiredLanes,
        [property: JsonPropertyName("proofs")] IReadOnlyList<ProofRow> Proofs,
        [property: JsonPropertyName("missing_proof_ids")] IReadOnlyList<string> MissingProofIds,
        [property: JsonPropertyName("ineligible_proof_ids")] IReadOnlyList<string> IneligibleProofIds,
        [property: JsonPropertyName("unavailable_proof_ids")] IReadOnlyList<string> UnavailableProofIds,
        [property: JsonPropertyName("present_proof_ids")] IReadOnlyList<string> PresentProofIds,
        [property: JsonPropertyName("diagnostics")] IReadOnlyList<Diagnostic> Diagnostics);

    internal sealed record Options(string? Unit, string? Domain, string? Team, string? Repo,
        string? HeadSha, string? PacketSha256, string Format, IReadOnlyList<(string ProofId, string RecordId)> References,
        string? Error);

    internal static int Execute(string repoRoot, string[] args, TextWriter writer,
        Action<string, string>? beforeOperation = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            writer.WriteLine("Read-only local coverage; it does not execute commands or verify producer, acceptance, authority, or publication.");
            return 0;
        }

        var options = Parse(args);
        var report = Evaluate(repoRoot, options, beforeOperation);
        Emit(writer, options.Format, report);
        return report.State == "complete" ? 0 : 1;
    }

    internal static Report Evaluate(string repoRoot, Options options,
        Action<string, string>? beforeOperation = null)
    {
        if (options.Error is not null)
        {
            return Empty(options, "invalid", "verification-coverage-argument-invalid", options.Error,
                null, null, null, [], [], [], [], [], [], [], [],
                [new("verification-coverage-argument-invalid", null, options.Error)]);
        }

        var packetOptions = new VerificationEvidenceCommand.Options(
            "coverage", null, options.Unit, options.Domain, options.Team, options.Repo,
            options.HeadSha, options.PacketSha256, null, null, "json", false, false, null);
        if (!VerificationEvidenceCommand.TryLoadPacket(repoRoot, packetOptions, beforeOperation, out var packet, out var packetFailure))
        {
            var failure = packetFailure!;
            var cause = failure.Cause == "verification-evidence-identity-conflict"
                ? "verification-coverage-identity-conflict"
                : "verification-coverage-packet-unavailable";
            var detail = SafeDetail(repoRoot, failure.Detail);
            return Empty(options, "unavailable", cause, detail, failure.PacketPath,
                failure.ObservedPacketSha256, null, [], [], [], [], [], [], [], [],
                failure.Diagnostics.Select(item => new Diagnostic("verification-coverage-packet-unavailable",
                    RelativePath(item.Path), SafeDetail(repoRoot, item.Detail))).ToArray());
        }

        var current = packet!;
        if (!string.Equals(current.Sha256, options.PacketSha256, StringComparison.Ordinal))
        {
            const string detail = "caller packet-sha256 does not match the exact observed packet bytes";
            return Empty(options, "unavailable", "verification-coverage-packet-digest-mismatch", detail,
                current.RelativePath, current.Sha256, null, current.Contract.MissingAcceptanceCriteria,
                current.Contract.MissingLanes, current.Contract.MissingLocalRequiredLanes, [], [], [], [],
                [], [new("verification-coverage-packet-digest-mismatch", current.RelativePath, detail)], current.Contract.State);
        }

        var proofMap = current.Contract.Proofs.ToDictionary(proof => proof.Id, StringComparer.Ordinal);
        foreach (var reference in options.References)
        {
            if (!proofMap.TryGetValue(reference.ProofId, out var proof))
            {
                var detail = $"proof reference '{reference.ProofId}' is not declared by the current packet";
                return Empty(options, "invalid", "verification-coverage-reference-invalid", detail,
                    current.RelativePath, current.Sha256, null, current.Contract.MissingAcceptanceCriteria,
                    current.Contract.MissingLanes, current.Contract.MissingLocalRequiredLanes, [], [], [], [],
                    [], [new("verification-coverage-reference-invalid", current.RelativePath, detail)], current.Contract.State);
            }
            if (proof.Evidence != "local-required")
            {
                var detail = $"proof '{reference.ProofId}' is hosted-required and cannot accept a local evidence reference";
                return Empty(options, "invalid", "verification-coverage-reference-invalid", detail,
                    current.RelativePath, current.Sha256, null, current.Contract.MissingAcceptanceCriteria,
                    current.Contract.MissingLanes, current.Contract.MissingLocalRequiredLanes, [], [], [], [],
                    [], [new("verification-coverage-reference-invalid", current.RelativePath, detail)], current.Contract.State);
            }
        }

        var references = options.References.ToDictionary(item => item.ProofId, item => item.RecordId, StringComparer.Ordinal);
        var rows = new List<ProofRow>(current.Contract.Proofs.Count);
        var rowDiagnostics = new List<Diagnostic>();
        foreach (var proof in current.Contract.Proofs.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (proof.Evidence == "hosted-required")
            {
                var criteria = proof.AcceptanceCriteria
                    .Select(id => current.Contract.AcceptanceCriteria.First(criterion => criterion.Id == id))
                    .OrderBy(criterion => criterion.Id, StringComparer.Ordinal)
                    .Select(criterion => new VerificationEvidenceCriterion(criterion.Id, criterion.CriterionIndex, criterion.Text))
                    .ToArray();
                var lane = current.Contract.Lanes.First(item => item.Id == proof.Lane);
                rows.Add(new ProofRow(proof.Id, criteria, lane.Id, lane.Weight, lane.Affected,
                    proof.Command, proof.Evidence, "not-applicable", "verification-coverage-hosted-not-observed",
                    null, "not-applicable", null, null, null, null, []));
                continue;
            }
            var binding = VerificationEvidenceCommand.BuildBinding(current.Contract, proof.Id)!;
            if (!references.TryGetValue(proof.Id, out var recordId))
            {
                rows.Add(Row(binding, "missing", "verification-coverage-reference-missing", null,
                    "unknown", null, null, null, null, []));
                continue;
            }

            var stored = new VerificationEvidenceStore(repoRoot, beforeOperation)
                .Read(options.Domain!, options.Team!, options.Unit!, recordId);
            if (stored.State == "missing")
            {
                rows.Add(Row(binding, "missing", "verification-coverage-record-missing", recordId,
                    "unknown", null, null, null, null,
                    [StoreDiagnostic(stored, repoRoot, "verification-coverage-record-missing")]));
                continue;
            }
            if (stored.State != "current" || stored.Record is null || stored.Path is null || stored.Sha256 is null)
            {
                var diagnostic = StoreDiagnostic(stored, repoRoot, "verification-coverage-record-unavailable");
                rows.Add(Row(binding, "unavailable", "verification-coverage-record-unavailable", recordId,
                    "unknown", null, null, null, null, [diagnostic]));
                rowDiagnostics.Add(diagnostic);
                continue;
            }

            var record = stored.Record;
            if (!string.Equals(record.TargetRepo, options.Repo, StringComparison.Ordinal))
            {
                const string detail = "stored evidence target_repo does not match the requested repository";
                var diagnostic = new Diagnostic("verification-coverage-record-unavailable", stored.Path, detail);
                rows.Add(Row(binding, "unavailable", "verification-coverage-record-unavailable", recordId,
                    "unknown", null, null, null, null, [diagnostic]));
                rowDiagnostics.Add(diagnostic);
                continue;
            }

            var storedBindingMatches = string.Equals(record.Binding.ProofId, proof.Id, StringComparison.Ordinal)
                && VerificationEvidenceCommand.BindingsEqual(binding, record.Binding);
            var currentBinding = storedBindingMatches
                && string.Equals(record.ChildHeadSha, options.HeadSha, StringComparison.Ordinal)
                && string.Equals(record.PacketSha256, options.PacketSha256, StringComparison.Ordinal);
            if (!currentBinding)
            {
                rows.Add(Row(binding, "ineligible", "verification-coverage-stale-binding", recordId,
                    "stale", stored.Path, stored.Sha256, record.TrxSha256, record.Report,
                    [new("verification-coverage-stale-binding", stored.Path,
                        "canonical evidence is verified but its caller-supplied head, packet, or proof binding is historical") ]));
                continue;
            }

            var report = record.Report;
            string? ineligibleCause = null;
            if (report.Failed > 0) ineligibleCause = "verification-coverage-reported-failed";
            else if (report.Skipped > 0) ineligibleCause = "verification-coverage-reported-skipped";
            else if (report.Passed == 0 || report.Total == 0) ineligibleCause = "verification-coverage-no-results";
            if (ineligibleCause is not null)
            {
                rows.Add(Row(binding, "ineligible", ineligibleCause, recordId, "current",
                    stored.Path, stored.Sha256, record.TrxSha256, report,
                    [new(ineligibleCause, stored.Path, "current bound report does not contain only passing nonempty results")]));
                continue;
            }
            rows.Add(Row(binding, "present", "verification-coverage-present", recordId, "current",
                stored.Path, stored.Sha256, record.TrxSha256, report, []));
        }

        var proofRows = rows.OrderBy(row => row.ProofId, StringComparer.Ordinal).ToArray();
        var missing = Ids(proofRows, "missing");
        var ineligible = Ids(proofRows, "ineligible");
        var unavailable = Ids(proofRows, "unavailable");
        var present = Ids(proofRows, "present");
        var hasUnavailable = unavailable.Length > 0;
        var structuralIncomplete = current.Contract.State != "complete";
        var topState = hasUnavailable ? "unavailable"
            : missing.Length > 0 || ineligible.Length > 0 || structuralIncomplete ? "incomplete" : "complete";
        var topCause = topState switch
        {
            "unavailable" => "verification-coverage-record-unavailable",
            "incomplete" => "verification-coverage-incomplete",
            _ => "verification-coverage-complete",
        };
        var topDetail = topState switch
        {
            "unavailable" => "one or more explicitly referenced local records could not be safely verified",
            "incomplete" => "one or more local proof rows or G865 structural requirements are incomplete",
            _ => "all local-required proof rows have current passing reports; hosted-required rows remain not observed",
        };
        var final = new Report(options.Unit, options.Domain, options.Team, options.Repo, options.HeadSha,
            options.PacketSha256, current.Sha256, current.RelativePath, topState, topCause, topDetail,
            "not-observed", "not-authenticated", "caller-supplied-not-authenticated", "not-assessed", "not-verified",
            current.Contract.State, current.Contract.MissingAcceptanceCriteria, current.Contract.MissingLanes,
            current.Contract.MissingLocalRequiredLanes, proofRows, missing, ineligible, unavailable, present,
            rowDiagnostics.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Cause, StringComparer.Ordinal).ToArray());
        return final;
    }

    internal static Options ParseArguments(string[] args) => Parse(args);

    internal static void WriteMarkdown(TextWriter writer, Report report)
    {
        writer.WriteLine("## Verification evidence coverage (read-only observation)");
        writer.WriteLine();
        writer.WriteLine($"- State: {report.State}");
        writer.WriteLine($"- Cause: {report.Cause}");
        writer.WriteLine($"- Execution: {report.Execution}; producer: {report.ProducerIdentity}; acceptance: {report.Acceptance}; publication: {report.Publication}");
        writer.WriteLine("- Detail:");
        WriteFence(writer, "text", report.Detail);
        writer.WriteLine("- Proof rows and diagnostics are included in the structured report below.");
        writer.WriteLine();
        WriteFence(writer, "json", JsonSerializer.Serialize(report, JsonOptions));
    }

    private static Options Parse(string[] args)
    {
        string format = "json";
        for (var index = 0; index + 1 < args.Length; index++)
            if (args[index] == "--format" && args[index + 1] is "json" or "markdown") { format = args[index + 1]; break; }
        string? unit = null, domain = null, team = null, repo = null, head = null, packet = null, suppliedFormat = null;
        string? error = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<(string ProofId, string RecordId)>();
        for (var index = 0; index < args.Length; index++)
        {
            var flag = args[index];
            if (flag is not ("--execution-unit" or "--domain" or "--team" or "--repo" or "--head-sha" or "--packet-sha256" or "--evidence" or "--format"))
            { error = $"unknown argument '{flag}'"; break; }
            if (index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            { error = $"{flag} requires a nonempty value"; break; }
            var value = args[++index];
            if (flag == "--evidence")
            {
                if (references.Count >= MaximumReferences) { error = $"at most {MaximumReferences} --evidence references are allowed"; break; }
                var equals = value.IndexOf('=');
                if (equals <= 0 || equals != value.LastIndexOf('=') || equals == value.Length - 1)
                { error = "--evidence must be exactly <proof-id>=<record-id>"; break; }
                references.Add((value[..equals], value[(equals + 1)..]));
                continue;
            }
            if (!seen.Add(flag)) { error = $"duplicate argument '{flag}'"; break; }
            switch (flag)
            {
                case "--execution-unit": unit = value; break;
                case "--domain": domain = value; break;
                case "--team": team = value; break;
                case "--repo": repo = value; break;
                case "--head-sha": head = value; break;
                case "--packet-sha256": packet = value; break;
                case "--format":
                    if (value is not ("json" or "markdown")) error = "--format must be json or markdown";
                    else suppliedFormat = value;
                    break;
            }
            if (error is not null) break;
        }
        format = suppliedFormat ?? format;
        foreach (var required in new[] { ("--execution-unit", unit), ("--domain", domain), ("--team", team),
                     ("--repo", repo), ("--head-sha", head), ("--packet-sha256", packet) })
            if (required.Item2 is null) error ??= $"{required.Item1} is required";
        if (unit is not null && !RulingArtifact.TryIdentifier(unit, out _)) error ??= "--execution-unit must be a canonical identifier";
        if (domain is not null && !RulingArtifact.TryIdentifier(domain, out _)) error ??= "--domain must be a canonical identifier";
        if (team is not null && !RulingArtifact.TryIdentifier(team, out _)) error ??= "--team must be a canonical identifier";
        if (repo is not null && !VerificationEvidenceCommand.IsValidRepo(repo)) error ??= "--repo must be an exact owner/repo identifier";
        if (head is not null && !VerificationEvidenceCommand.IsLowerHex(head, 40)) error ??= "--head-sha must be exactly 40 lowercase hexadecimal characters";
        if (packet is not null && !VerificationEvidenceCommand.IsLowerHex(packet, 64)) error ??= "--packet-sha256 must be exactly 64 lowercase hexadecimal characters";
        if (references.Any(item => !ValidProofId(item.ProofId) || !RulingArtifact.TryIdentifier(item.RecordId, out _)))
            error ??= "--evidence references must use canonical proof and record identifiers";
        if (references.Select(item => item.ProofId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != references.Count)
            error ??= "proof references must be unique without case aliases";
        if (references.Select(item => item.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != references.Count)
            error ??= "record references must be unique without case aliases";
        return new(unit, domain, team, repo, head, packet, format, references, error);
    }

    private static ProofRow Row(VerificationEvidenceBinding binding, string state, string cause,
        string? recordId, string bindingState, string? artifactPath, string? artifactSha, string? trxSha,
        TrxEvidenceReader.Summary? report, IReadOnlyList<Diagnostic> diagnostics) =>
        new(binding.ProofId, binding.AcceptanceCriteria, binding.LaneId, binding.LaneWeight, binding.LaneAffected,
            binding.Command, binding.Evidence, state, cause, recordId, bindingState, artifactPath,
            artifactSha, trxSha, report, diagnostics);

    private static Diagnostic StoreDiagnostic(VerificationEvidenceStoreRead stored, string root, string cause)
    {
        var detail = $"underlying store cause {stored.Cause}: {SafeDetail(root, stored.Detail)}";
        return new(cause, RelativePath(stored.Path), detail);
    }

    private static string[] Ids(IEnumerable<ProofRow> rows, string state) => rows
        .Where(row => row.State == state).Select(row => row.ProofId).OrderBy(item => item, StringComparer.Ordinal).ToArray();

    private static Report Empty(Options options, string state, string cause, string detail,
        string? packetPath, string? observedSha, string? structuralState,
        IReadOnlyList<string> missingAcs, IReadOnlyList<string> missingLanes,
        IReadOnlyList<string> missingLocalLanes, IReadOnlyList<ProofRow> proofs,
        IReadOnlyList<string> missing, IReadOnlyList<string> ineligible,
        IReadOnlyList<string> unavailable, IReadOnlyList<string> present,
        IReadOnlyList<Diagnostic> diagnostics, string? knownStructuralState = null) =>
        new(options.Unit, options.Domain, options.Team, options.Repo, options.HeadSha, options.PacketSha256,
            observedSha, packetPath, state, cause, detail, "not-observed", "not-authenticated",
            "caller-supplied-not-authenticated", "not-assessed", "not-verified", structuralState ?? knownStructuralState,
            missingAcs, missingLanes, missingLocalLanes, proofs, missing, ineligible, unavailable, present, diagnostics);

    private static bool ValidProofId(string value) => value.Length is >= 1 and <= 64
        && value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        && value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
            or >= '0' and <= '9' or '_' or '-');

    private static string? RelativePath(string? path) => string.IsNullOrEmpty(path) ? null : path.Replace('\\', '/');

    private static string SafeDetail(string root, string detail)
    {
        var full = Path.GetFullPath(root);
        return detail.Replace(full, "<repository>", StringComparison.Ordinal)
            .Replace(full.Replace('\\', '/'), "<repository>", StringComparison.Ordinal);
    }

    private static void Emit(TextWriter writer, string format, Report report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        if (format == "json") { writer.WriteLine(json); return; }
        writer.WriteLine("# Verification evidence coverage");
        writer.WriteLine();
        writer.WriteLine($"- State: {report.State}");
        writer.WriteLine($"- Cause: {report.Cause}");
        writer.WriteLine("- Execution: not observed; producer: not authenticated; acceptance: not assessed; publication: not verified.");
        writer.WriteLine("- Detail:");
        WriteFence(writer, "text", report.Detail);
        writer.WriteLine();
        writer.WriteLine("## Structured report (same facts as JSON)");
        writer.WriteLine();
        WriteFence(writer, "json", json);
    }

    private static void WriteFence(TextWriter writer, string language, string value)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in value)
        {
            if (character == '`') { current++; longest = Math.Max(longest, current); }
            else current = 0;
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        writer.WriteLine(fence + language);
        writer.WriteLine(value);
        writer.WriteLine(fence);
    }
}
