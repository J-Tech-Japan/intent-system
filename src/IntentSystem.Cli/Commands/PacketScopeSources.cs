using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>Read-only validation of packet-pinned G862 ruling sources.</summary>
internal static class PacketScopeSources
{
    private const string OpenMarker = "<!-- intent-cli:scope-sources:v1 -->";
    private const string CloseMarker = "<!-- /intent-cli:scope-sources:v1 -->";
    private static readonly Regex DigestPattern = new("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal sealed record ProvenanceEntry(
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("domain")] string Domain,
        [property: JsonPropertyName("team")] string Team,
        [property: JsonPropertyName("target_repo")] string TargetRepo,
        [property: JsonPropertyName("execution_unit")] string ExecutionUnit,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonIgnore] string CanonicalRecordJson);

    internal sealed record Result(
        [property: JsonPropertyName("execution_unit")] string ExecutionUnit,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("evaluated_at")] string EvaluatedAt,
        [property: JsonPropertyName("provenance")] IReadOnlyList<ProvenanceEntry>? Provenance,
        [property: JsonPropertyName("expected_provenance_block")] string? ExpectedProvenanceBlock,
        [property: JsonPropertyName("diagnostics")] IReadOnlyList<RulingDiagnostic> Diagnostics,
        [property: JsonPropertyName("authority_verification")] string AuthorityVerification = "supplied-not-authenticated",
        [property: JsonPropertyName("publication")] string Publication = "not-verified",
        [property: JsonIgnore] bool IsDeclared = false)
    {
        [JsonIgnore]
        public bool IsSuccessful => State is "not-declared" or "satisfied";
        [JsonIgnore] public string? SourceDomain { get; init; }
        [JsonIgnore] public string? SourceTeam { get; init; }
        [JsonIgnore] public string? SourceTargetRepo { get; init; }
    }

    internal static Result Evaluate(string repoRoot, string executionUnit, byte[] packetYaml, byte[] githubBody,
        DateTimeOffset now, Action<string, string>? beforeRead = null)
    {
        var timestamp = RulingArtifact.FormatTimestamp(now);
        if (!TryParsePacket(packetYaml, out var root, out var parseError))
        {
            var duplicate = FindDuplicateParticipatingKey(packetYaml);
            if (duplicate == DuplicateKeyKind.SourceDeclaration)
                return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "packet contains duplicate source or pin keys", []);
            if (duplicate == DuplicateKeyKind.SourceIdentity)
                return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "packet contains duplicate source identity keys", []);
            return Unavailable(executionUnit, timestamp, "scope-sources-packet-unavailable", parseError,
                [new("packet-unparseable", null, parseError)]);
        }

        var rootEntries = Entries(root!);
        if (HasDuplicateRelevantKeys(rootEntries, ["scope_sources", "scope_source_digests"]))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "packet root contains duplicate source keys", []);
        if (HasInvalidRelevantKeyTag(rootEntries, ["scope_sources", "scope_source_digests"]))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "source keys must be plain textual keys", []);

        // Misplaced source declarations are always invalid, including inside legacy identity
        // metadata. Identity validation itself is opt-in and is intentionally skipped for an
        // absent or explicitly empty source list.
        var nestedIdentities = rootEntries.Where(entry => IsKey(entry.Key, "implementation_issue_packet")).Select(entry => entry.Value).ToArray();
        if (nestedIdentities.OfType<YamlMappingNode>().Any(identityMap => Entries(identityMap)
            .Any(entry => IsKey(entry.Key, "scope_sources") || IsKey(entry.Key, "scope_source_digests"))))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "source declarations belong at the packet root", []);

        var sourcesNode = FindUnique(rootEntries, "scope_sources", out var sourcesDuplicate);
        var pinsNode = FindUnique(rootEntries, "scope_source_digests", out var pinsDuplicate);
        if (sourcesDuplicate || pinsDuplicate)
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "source declarations contain duplicate keys", []);
        if ((sourcesNode is not null || pinsNode is not null)
            && HasInvalidParticipatingAlias(StrictUtf8.GetString(packetYaml), [], inspectIdentity: false))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "aliases are not accepted in source declarations", []);

        if (sourcesNode is null && pinsNode is null)
            return NotDeclared(executionUnit, timestamp);

        if (sourcesNode is not YamlSequenceNode sourceSequence)
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_sources must be a sequence", []);
        if (!IsImplicitOrExplicitSequence(sourceSequence))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_sources must not use a tag or alias", []);
        if (sourceSequence.Children.Count == 0)
        {
            if (pinsNode is null || pinsNode is YamlMappingNode emptyPins && IsImplicitOrExplicitMap(emptyPins) && Entries(emptyPins).Count == 0)
                return NotDeclared(executionUnit, timestamp);
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "empty scope_sources requires absent or empty scope_source_digests", []);
        }
        if (HasInvalidParticipatingAlias(StrictUtf8.GetString(packetYaml), ["source_execution_unit", "domain", "team", "target_repo"]))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "aliases are not accepted in source declarations or source identity", []);
        if (HasDuplicateRelevantKeys(rootEntries, ["implementation_issue_packet"]))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "packet root contains duplicate identity keys", []);
        if (HasInvalidRelevantKeyTag(rootEntries, ["implementation_issue_packet"]))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "identity key must be a plain textual key", []);
        var nestedIdentity = nestedIdentities.SingleOrDefault();
        if (sourceSequence.Children.Count > 16)
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_sources supports at most 16 references", []);
        if (!IsImplicitOrExplicitMapNode(pinsNode))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_source_digests must be a mapping", []);

        var references = new List<string>();
        foreach (var node in sourceSequence.Children)
        {
            if (!TryGetScalarString(node, out var reference))
                return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "each scope_sources item must be a textual scalar", []);
            if (!reference.StartsWith("ruling:", StringComparison.Ordinal)
                || !RulingArtifact.TryIdentifier(reference[7..], out _))
                return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", $"invalid ruling reference '{reference}'", []);
            references.Add(reference);
        }
        if (references.Distinct(StringComparer.Ordinal).Count() != references.Count
            || references.Distinct(StringComparer.OrdinalIgnoreCase).Count() != references.Count)
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_sources contains duplicate or case-alias references", []);

        var pinEntries = Entries((YamlMappingNode)pinsNode!);
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in pinEntries)
        {
            if (!TryGetScalarString(entry.Key, out var key) || !TryGetScalarString(entry.Value, out var digest))
                return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_source_digests keys and values must be textual scalars", []);
            if (!pins.TryAdd(key, digest))
                return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_source_digests contains duplicate keys", []);
        }
        if (pins.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pins.Count
            || pins.Count != references.Count
            || references.Any(reference => !pins.ContainsKey(reference))
            || pins.Keys.Any(key => !references.Contains(key, StringComparer.Ordinal)))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "scope_source_digests must contain exactly one pin for each source reference", []);
        if (pins.Values.Any(digest => !DigestPattern.IsMatch(digest)))
            return Refused(executionUnit, timestamp, "scope-sources-invalid-declaration", "each ruling digest must be 64 lowercase hexadecimal characters", []);

        if (nestedIdentity is not YamlMappingNode identity || !IsImplicitOrExplicitMap(identity))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "implementation_issue_packet identity mapping is required for pinned sources", []);
        var identityEntries = Entries(identity);
        if (HasInvalidRelevantKeyTag(identityEntries, ["source_execution_unit", "domain", "team", "target_repo"]))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "source identity keys must be plain textual keys", []);
        if (!TryIdentity(identityEntries, out var sourceUnit, out var domain, out var team, out var targetRepo, out var identityError))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", identityError, []);
        if (!StringComparer.Ordinal.Equals(sourceUnit, executionUnit))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "source_execution_unit must exactly match --execution-unit", []);
        if (!RulingArtifact.TryIdentifier(executionUnit, out _))
            return Refused(executionUnit, timestamp, "scope-sources-identity-mismatch", "--execution-unit must be a canonical identifier", []);

        var validated = new List<ProvenanceEntry>(references.Count);
        foreach (var reference in references.Order(StringComparer.Ordinal))
        {
            var id = reference[7..];
            var evaluation = new RulingArtifactStore(repoRoot, beforeRead).Evaluate(domain, team, id, now);
            if (evaluation.Status == "missing")
                return Refused(executionUnit, timestamp, "scope-sources-ruling-missing", $"pinned ruling '{reference}' is missing", EvidenceDiagnostics(evaluation, domain, team, id)) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
            if (evaluation.Status == "conflict")
                return Refused(executionUnit, timestamp, "scope-sources-ruling-conflict", evaluation.Detail, EvidenceDiagnostics(evaluation, domain, team, id)) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
            if (evaluation.Status == "unavailable")
                return Unavailable(executionUnit, timestamp, "scope-sources-ruling-unavailable", evaluation.Detail, EvidenceDiagnostics(evaluation, domain, team, id)) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
            if (evaluation.Status != "active")
                return Refused(executionUnit, timestamp, "scope-sources-ruling-inactive", $"pinned ruling '{reference}' is {evaluation.Status}", EvidenceDiagnostics(evaluation, domain, team, id)) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };

            var record = evaluation.Records.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Id, id));
            if (record is null)
                return Unavailable(executionUnit, timestamp, "scope-sources-ruling-unavailable", "active ruling evaluation did not contain its exact target", evaluation.Diagnostics) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
            if (!StringComparer.Ordinal.Equals(record.TargetRepo, targetRepo)
                || (record.ScopeKind == "execution-units" && !record.ExecutionUnits.Contains(executionUnit, StringComparer.Ordinal)))
                return Refused(executionUnit, timestamp, "scope-sources-applicability-mismatch", $"pinned ruling '{reference}' does not apply to this repository and execution unit", evaluation.Diagnostics) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };

            var actualDigest = Convert.ToHexString(SHA256.HashData(RulingArtifact.Serialize(record))).ToLowerInvariant();
            if (!StringComparer.Ordinal.Equals(actualDigest, pins[reference]))
                return Refused(executionUnit, timestamp, "scope-sources-digest-mismatch", $"pinned digest for '{reference}' does not match the active canonical ruling", evaluation.Diagnostics) with { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
            validated.Add(new(reference, domain, team, targetRepo, executionUnit, record.RelativePath,
                actualDigest, Encoding.UTF8.GetString(RulingArtifact.Serialize(record))));
        }

        var expected = FormatProvenanceBlock(validated);
        var body = DecodeBody(githubBody, out var bodyDecodeError);
        if (body is null)
            return Unavailable(executionUnit, timestamp, "scope-sources-packet-unavailable", bodyDecodeError!,
                [new("packet-unparseable", "github-body.md", bodyDecodeError!)]) with
            {
                Provenance = validated,
                ExpectedProvenanceBlock = expected,
                IsDeclared = true,
                SourceDomain = domain,
                SourceTeam = team,
                SourceTargetRepo = targetRepo,
            };
        if (!HasExactProvenanceBlock(body, expected))
            return new(executionUnit, "refused", "scope-sources-provenance-mismatch",
                "github-body.md must contain exactly one canonical scope provenance block", timestamp,
                validated, expected, [] , IsDeclared: true) { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
        return new(executionUnit, "satisfied", "scope-sources-satisfied", "all pinned active rulings and public provenance match",
            timestamp, validated, expected, [], IsDeclared: true) { SourceDomain = domain, SourceTeam = team, SourceTargetRepo = targetRepo };
    }

    internal static bool HasDeclarationOrMisplacedDeclaration(byte[] packetYaml, bool malformedMeansDeclaration = true)
    {
        if (!TryParseFirstPacketDocument(packetYaml, out var root, out _))
        {
            // A malformed packet cannot establish that the optional declaration
            // is absent. Route it through the evaluator so callers fail closed
            // with the ordinary packet-unavailable result.
            return malformedMeansDeclaration;
        }
        var entries = Entries(root!);
        if (entries.Where(entry => IsKey(entry.Key, "implementation_issue_packet"))
            .Select(entry => entry.Value).OfType<YamlMappingNode>()
            .Any(identity => Entries(identity).Any(entry => IsKey(entry.Key, "scope_sources") || IsKey(entry.Key, "scope_source_digests"))))
            return true;

        var sources = FindUnique(entries, "scope_sources", out var sourcesDuplicate);
        var pins = FindUnique(entries, "scope_source_digests", out var pinsDuplicate);
        if (sourcesDuplicate || pinsDuplicate) return true;
        if (HasInvalidRelevantKeyTag(entries, ["scope_sources", "scope_source_digests"])) return true;
        if ((sources is not null || pins is not null)
            && HasInvalidParticipatingAlias(StrictUtf8.GetString(packetYaml), [], inspectIdentity: false)) return true;
        if (sources is YamlSequenceNode sequence
            && sequence.Children.Count == 0
            && IsImplicitOrExplicitSequence(sequence)
            && (pins is null || pins is YamlMappingNode pinMap
                && IsImplicitOrExplicitMap(pinMap)
                && Entries(pinMap).Count == 0))
            return false;

        return sources is not null || pins is not null;
    }

    internal static bool HasDeclarationOrMisplacedDeclaration(string packetYaml)
        => HasDeclarationOrMisplacedDeclaration(packetYaml, out _);

    /// <summary>
    /// Determines whether callers must inspect the original packet bytes, while separately
    /// reporting whether this text positively contains a source declaration. A replacement
    /// character may be valid UTF-8 or the result of a lossy text read, so it cannot prove
    /// either presence or absence on its own.
    /// </summary>
    internal static bool HasDeclarationOrMisplacedDeclaration(string packetYaml, out bool observedDeclaration)
    {
        observedDeclaration = false;
        try
        {
            var bytes = StrictUtf8.GetBytes(packetYaml);
            observedDeclaration = HasDeclarationOrMisplacedDeclaration(bytes, malformedMeansDeclaration: false);

            // Text obtained through the legacy title reader may have been decoded with
            // replacement fallback. Route it to a raw-byte probe, but do not mistake a
            // valid literal U+FFFD in a legacy packet for an observed opt-in declaration.
            return packetYaml.Contains('\uFFFD') || HasDeclarationOrMisplacedDeclaration(bytes);
        }
        catch (EncoderFallbackException)
        {
            return true;
        }
    }

    internal static string FormatProvenanceBlock(IReadOnlyList<ProvenanceEntry> provenance)
    {
        var lines = new List<string>
        {
            OpenMarker,
            "### Ruling scope provenance",
        };
        lines.AddRange(provenance.OrderBy(item => item.Reference, StringComparer.Ordinal).Select(item =>
            $"- {item.Reference} | domain={item.Domain} | team={item.Team} | repo={item.TargetRepo} | unit={item.ExecutionUnit} | sha256={item.Sha256}"));
        lines.Add("Authority: supplied-not-authenticated. Publication: not-verified.");
        lines.Add(CloseMarker);
        return string.Join("\n", lines);
    }

    private static bool HasExactProvenanceBlock(string body, string expected)
    {
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var openCount = Count(normalized, OpenMarker);
        var closeCount = Count(normalized, CloseMarker);
        if (openCount != 1 || closeCount != 1) return false;
        var index = normalized.IndexOf(expected, StringComparison.Ordinal);
        if (index < 0 || normalized.IndexOf(expected, index + expected.Length, StringComparison.Ordinal) >= 0) return false;
        var end = index + expected.Length;
        return (index == 0 || normalized[index - 1] == '\n')
            && (end == normalized.Length || normalized[end] == '\n');
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var offset = 0; (offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0; offset += needle.Length) count++;
        return count;
    }

    private static bool TryParsePacket(byte[] bytes, out YamlMappingNode? root, out string error)
    {
        root = null;
        error = string.Empty;
        try
        {
            var text = StrictUtf8.GetString(bytes);
            var stream = new YamlStream();
            using var reader = new StringReader(text);
            stream.Load(reader);
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                error = "packet.yaml must contain exactly one root mapping";
                return false;
            }
            root = mapping;
            return true;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }

    // Legacy packet consumers project the first root mapping from a valid YAML
    // stream. Use that same boundary only to decide whether source evaluation is
    // needed. Once a non-empty source declaration opts in, Evaluate still uses
    // TryParsePacket and enforces the stricter single-document source contract.
    private static bool TryParseFirstPacketDocument(byte[] bytes, out YamlMappingNode? root, out string error)
    {
        root = null;
        error = string.Empty;
        try
        {
            var text = StrictUtf8.GetString(bytes);
            var stream = new YamlStream();
            using var reader = new StringReader(text);
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                error = "packet.yaml must contain a root mapping";
                return false;
            }
            root = mapping;
            return true;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }

    private enum DuplicateKeyKind
    {
        None,
        SourceDeclaration,
        SourceIdentity,
    }

    private sealed record RelevantYamlScan(bool HasSourceDeclaration, DuplicateKeyKind DuplicateKey);

    private static DuplicateKeyKind FindDuplicateParticipatingKey(byte[] bytes) => ScanRelevantYaml(bytes).DuplicateKey;

    // YamlStream deliberately rejects duplicate mapping keys before it exposes the
    // representation tree. Keep this event-only pass narrow: duplicates in unrelated
    // packet metadata retain the existing packet parser's behavior.
    private static RelevantYamlScan ScanRelevantYaml(byte[] bytes)
    {
        var hasSourceDeclaration = false;
        var duplicate = DuplicateKeyKind.None;
        var stack = new Stack<YamlEventFrame>();
        try
        {
            var yaml = StrictUtf8.GetString(bytes);
            var parser = new Parser(new StringReader(yaml));
            while (parser.MoveNext())
            {
                switch (parser.Current)
                {
                    case MappingStart:
                    {
                        var path = ConsumeNode(stack, null);
                        stack.Push(new YamlEventFrame(true, path));
                        break;
                    }
                    case SequenceStart:
                    {
                        var path = ConsumeNode(stack, null);
                        stack.Push(new YamlEventFrame(false, path));
                        break;
                    }
                    case Scalar scalar:
                    {
                        var isMapKey = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        if (isMapKey && scalar.Value is { } key)
                        {
                            var mapPath = stack.Peek().Path;
                            var relevantDeclaration = mapPath.Length == 0 && key is "scope_sources" or "scope_source_digests"
                                || mapPath == "/implementation_issue_packet" && key is "scope_sources" or "scope_source_digests";
                            if (relevantDeclaration) hasSourceDeclaration = true;

                            if (mapPath.Length == 0 && key is "scope_sources" or "scope_source_digests"
                                || mapPath == "/scope_source_digests" && key.StartsWith("ruling:", StringComparison.Ordinal))
                            {
                                if (!stack.Peek().SeenKeys.Add(key)) duplicate = DuplicateKeyKind.SourceDeclaration;
                            }
                            else if (mapPath.Length == 0 && key == "implementation_issue_packet"
                                || mapPath == "/implementation_issue_packet"
                                    && key is "source_execution_unit" or "domain" or "team" or "target_repo")
                            {
                                if (!stack.Peek().SeenKeys.Add(key) && duplicate == DuplicateKeyKind.None)
                                    duplicate = DuplicateKeyKind.SourceIdentity;
                            }
                            ConsumeNode(stack, key);
                        }
                        else
                        {
                            ConsumeNode(stack, null);
                        }
                        break;
                    }
                    case AnchorAlias alias:
                    {
                        var isMapKey = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        var aliasKey = alias.Value.ToString();
                        if (isMapKey && aliasKey is "scope_sources" or "scope_source_digests")
                            hasSourceDeclaration = true;
                        ConsumeNode(stack, isMapKey ? aliasKey : null);
                        break;
                    }
                    case MappingEnd:
                    case SequenceEnd:
                        if (stack.Count > 0) stack.Pop();
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is DecoderFallbackException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            // Keep any relevant key/duplicate already observed before the malformed
            // suffix; a malformed declaration is not equivalent to legacy omission.
        }
        return new(hasSourceDeclaration, duplicate);
    }

    private static bool TryIdentity(IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> entries,
        out string unit, out string domain, out string team, out string targetRepo, out string error)
    {
        unit = domain = team = targetRepo = string.Empty;
        error = "implementation_issue_packet must contain canonical source identity scalars";
        if (HasDuplicateRelevantKeys(entries, ["source_execution_unit", "domain", "team", "target_repo"])) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Key is not YamlScalarNode keyNode || keyNode.Value is not { } key) continue;
            if (key is not ("source_execution_unit" or "domain" or "team" or "target_repo")) continue;
            if (!IsImplicitOrExplicitString(keyNode)) return false;
            if (!TryGetScalarString(entry.Value, out var value)) return false;
            values[key] = value;
        }
        if (!values.TryGetValue("source_execution_unit", out var parsedUnit)
            || !values.TryGetValue("domain", out var parsedDomain)
            || !values.TryGetValue("team", out var parsedTeam)
            || !values.TryGetValue("target_repo", out var parsedTargetRepo)
            || !RulingArtifact.TryIdentifier(parsedUnit, out _)
            || !RulingArtifact.TryIdentifier(parsedDomain, out _)
            || !RulingArtifact.TryIdentifier(parsedTeam, out _)
            || !CrossRuntimeReviewPaths.IsRepositoryName(parsedTargetRepo)) return false;
        unit = parsedUnit;
        domain = parsedDomain;
        team = parsedTeam;
        targetRepo = parsedTargetRepo;
        error = string.Empty;
        return true;
    }

    private static bool HasDuplicateRelevantKeys(IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> entries, IReadOnlyCollection<string> relevant)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (entry.Key is YamlScalarNode { Value: { } key } && relevant.Contains(key, StringComparer.Ordinal) && !seen.Add(key)) return true;
        return false;
    }

    private static bool HasInvalidRelevantKeyTag(IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> entries, IReadOnlyCollection<string> relevant) =>
        entries.Any(entry => entry.Key is YamlScalarNode { Value: { } key } && relevant.Contains(key, StringComparer.Ordinal)
            && !IsImplicitOrExplicitString(entry.Key));

    private static YamlNode? FindUnique(IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> entries, string key, out bool duplicate)
    {
        duplicate = false;
        YamlNode? result = null;
        foreach (var entry in entries)
        {
            if (!IsKey(entry.Key, key)) continue;
            if (result is not null) { duplicate = true; return null; }
            result = entry.Value;
        }
        return result;
    }

    private static bool IsKey(YamlNode node, string key) => node is YamlScalarNode { Value: { } value } && StringComparer.Ordinal.Equals(value, key);
    private static IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> Entries(YamlMappingNode mapping) => mapping.Children.ToList();

    private static bool TryGetScalarString(YamlNode node, out string value)
    {
        value = string.Empty;
        if (node is not YamlScalarNode scalar || !IsImplicitOrExplicitString(scalar)) return false;
        if (IsImplicitNull(scalar)) return false;
        value = scalar.Value ?? string.Empty;
        return true;
    }

    private static bool IsImplicitOrExplicitString(YamlNode node)
    {
        var tag = node.Tag.ToString();
        return tag is "?" or "!" or "tag:yaml.org,2002:str";
    }

    private static bool IsImplicitNull(YamlScalarNode scalar) =>
        scalar.Tag.ToString() != "tag:yaml.org,2002:str"
        && scalar.Style == YamlDotNet.Core.ScalarStyle.Plain
        && (string.IsNullOrEmpty(scalar.Value) || scalar.Value == "~" || string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase));

    private static bool IsImplicitOrExplicitMap(YamlMappingNode node) => node.Tag.ToString() is "?" or "!" or "tag:yaml.org,2002:map";
    private static bool IsImplicitOrExplicitSequence(YamlSequenceNode node) => node.Tag.ToString() is "?" or "!" or "tag:yaml.org,2002:seq";
    private static bool IsImplicitOrExplicitMapNode(YamlNode? node) => node is YamlMappingNode map && IsImplicitOrExplicitMap(map);

    private static Result NotDeclared(string unit, string timestamp) => new(unit, "not-declared", "scope-sources-not-declared",
        "packet has no pinned ruling sources", timestamp, null, null, []);

    private static Result Refused(string unit, string timestamp, string cause, string detail, IReadOnlyList<RulingDiagnostic> diagnostics) =>
        new(unit, "refused", cause, detail, timestamp, null, null, diagnostics, IsDeclared: true);

    private static Result Unavailable(string unit, string timestamp, string cause, string detail, IReadOnlyList<RulingDiagnostic> diagnostics) =>
        new(unit, "unavailable", cause, detail, timestamp, null, null, diagnostics, IsDeclared: true);

    private static string? DecodeBody(byte[] bytes, out string? error)
    {
        try { error = null; return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { error = exception.Message; return null; }
    }

    private static IReadOnlyList<RulingDiagnostic> EvidenceDiagnostics(RulingEvaluation evaluation, string domain, string team, string id)
    {
        if (evaluation.Diagnostics.Count > 0) return evaluation.Diagnostics;
        var cause = evaluation.Cause.Length > 0 ? evaluation.Cause : evaluation.Status switch
        {
            "missing" => "ruling-not-found",
            "superseded" => "ruling-superseded",
            "expired" => "ruling-expired",
            _ => "ruling-evaluation-refused",
        };
        return [new(cause, $".intent-cli/rulings/{domain}/{team}/{id}.json", evaluation.Detail)];
    }

    private static bool HasInvalidParticipatingAlias(string yaml, IReadOnlyCollection<string> identityNames, bool inspectIdentity = true)
    {
        try
        {
            var parser = new Parser(new StringReader(yaml));
            var stack = new Stack<YamlEventFrame>();
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
            while (parser.MoveNext())
            {
                switch (parser.Current)
                {
                    case MappingStart:
                    {
                        var path = ConsumeNode(stack, null);
                        stack.Push(new YamlEventFrame(true, path));
                        break;
                    }
                    case SequenceStart:
                    {
                        var path = ConsumeNode(stack, null);
                        stack.Push(new YamlEventFrame(false, path));
                        break;
                    }
                    case Scalar scalar:
                    {
                        var path = CurrentChildPath(stack);
                        var anchor = scalar.Anchor.ToString();
                        if (anchor.Length > 0 && !string.IsNullOrEmpty(scalar.Value)) anchors[anchor] = scalar.Value;
                        var isMapKey = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        if (isMapKey) ConsumeNode(stack, scalar.Value);
                        else ConsumeNode(stack, null);
                        break;
                    }
                    case AnchorAlias alias:
                    {
                        var path = CurrentChildPath(stack);
                        var anchorName = alias.Value.ToString();
                        var keyAlias = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        if (IsParticipatingAliasPath(path, identityNames, inspectIdentity)
                            || keyAlias && anchors.TryGetValue(anchorName, out var aliasKey)
                                && IsParticipatingAliasPath(CurrentMapPath(stack) + "/" + EscapePathSegment(aliasKey), identityNames, inspectIdentity)) return true;
                        ConsumeNode(stack, keyAlias && anchors.TryGetValue(anchorName, out var keyValue) ? keyValue : null);
                        break;
                    }
                    case MappingEnd:
                    case SequenceEnd:
                        if (stack.Count > 0) stack.Pop();
                        break;
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class YamlEventFrame(bool isMapping, string path)
    {
        public bool IsMapping { get; } = isMapping;
        public string Path { get; } = path;
        public HashSet<string> SeenKeys { get; } = new(StringComparer.Ordinal);
        public int NextIndex { get; set; }
        public bool ExpectingKey { get; set; } = isMapping;
        public string? CurrentKey { get; set; }
    }

    private static string CurrentChildPath(Stack<YamlEventFrame> stack)
    {
        if (stack.Count == 0) return string.Empty;
        var frame = stack.Peek();
        if (frame.IsMapping) return frame.ExpectingKey ? frame.Path + "/<key>" : frame.Path + "/" + EscapePathSegment(frame.CurrentKey ?? "<value>");
        return frame.Path + "/" + frame.NextIndex;
    }

    private static string CurrentMapPath(Stack<YamlEventFrame> stack) => stack.Count == 0 ? string.Empty : stack.Peek().Path;

    private static string EscapePathSegment(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string ConsumeNode(Stack<YamlEventFrame> stack, string? scalarKey)
    {
        var path = CurrentChildPath(stack);
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

    private static bool IsParticipatingAliasPath(string path, IReadOnlyCollection<string> identityNames, bool inspectIdentity)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (parts[0] is "scope_sources" or "scope_source_digests") return true;
        if (!inspectIdentity || parts[0] != "implementation_issue_packet") return false;
        return parts.Length == 1
            || parts.Length == 2 && identityNames.Contains(parts[1], StringComparer.Ordinal)
            || (parts.Length == 2 && parts[1] is ("scope_sources" or "scope_source_digests"));
    }
}
