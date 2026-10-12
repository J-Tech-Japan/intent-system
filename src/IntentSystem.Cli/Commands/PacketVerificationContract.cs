using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>Bounded packet-local structural coverage for an explicitly authored verification contract.</summary>
internal static class PacketVerificationContract
{
    internal const int MaximumBytes = 1024 * 1024;
    internal const int MaximumDepth = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex IdentifierPattern = new(@"\A[A-Za-z][A-Za-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant);

    internal sealed record AcceptanceCriterion(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("criterion_index")] int CriterionIndex,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("proof_ids")] IReadOnlyList<string> ProofIds);

    internal sealed record Lane(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("weight")] string Weight,
        [property: JsonPropertyName("affected")] bool Affected,
        [property: JsonPropertyName("proof_ids")] IReadOnlyList<string> ProofIds);

    internal sealed record Proof(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("acceptance_criteria")] IReadOnlyList<string> AcceptanceCriteria,
        [property: JsonPropertyName("lane")] string Lane,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("evidence")] string Evidence,
        [property: JsonPropertyName("execution")] string Execution = "not-observed");

    internal sealed record Diagnostic(
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("detail")] string Detail);

    internal sealed record Result(
        [property: JsonPropertyName("execution_unit")] string ExecutionUnit,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("packet_path")] string? PacketPath,
        [property: JsonPropertyName("packet_sha256")] string? PacketSha256,
        [property: JsonPropertyName("execution")] string Execution,
        [property: JsonPropertyName("acceptance_criteria")] IReadOnlyList<AcceptanceCriterion> AcceptanceCriteria,
        [property: JsonPropertyName("lanes")] IReadOnlyList<Lane> Lanes,
        [property: JsonPropertyName("proofs")] IReadOnlyList<Proof> Proofs,
        [property: JsonPropertyName("missing_acceptance_criteria")] IReadOnlyList<string> MissingAcceptanceCriteria,
        [property: JsonPropertyName("missing_lanes")] IReadOnlyList<string> MissingLanes,
        [property: JsonPropertyName("missing_local_required_lanes")] IReadOnlyList<string> MissingLocalRequiredLanes,
        [property: JsonPropertyName("diagnostics")] IReadOnlyList<Diagnostic> Diagnostics);

    internal static Result Evaluate(string executionUnit, string packetPath, byte[] bytes)
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string yaml;
        try
        {
            yaml = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            return UnavailableWithBytes(executionUnit, packetPath, sha256, "packet-unparseable", exception.Message);
        }

        // A single leading BOM is part of the observed bytes/hash, but not YAML content.
        if (yaml.Length > 0 && yaml[0] == '\uFEFF') yaml = yaml[1..];

        var scan = ScanYaml(yaml);
        if (scan.DepthExceeded)
            return UnavailableWithBytes(executionUnit, packetPath, sha256, "resource-limit", $"packet YAML exceeds collection depth {MaximumDepth}");
        if (scan.ParseError is not null)
            return UnavailableWithBytes(executionUnit, packetPath, sha256, "packet-unparseable", scan.ParseError);
        if (scan.DocumentCount != 1)
            return UnavailableWithBytes(executionUnit, packetPath, sha256, "packet-unparseable", "packet.yaml must contain exactly one YAML document");
        if (scan.DuplicateParticipatingKey)
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "packet contains a duplicate participating mapping key");

        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
                return UnavailableWithBytes(executionUnit, packetPath, sha256, "packet-unparseable", "packet.yaml must contain exactly one root mapping");
            root = mapping;
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException or ArgumentException)
        {
            return UnavailableWithBytes(executionUnit, packetPath, sha256, "packet-unparseable", exception.Message);
        }

        var rootEntries = root.Children.ToArray();
        var contractEntry = FindEntry(rootEntries, "verification_contract");
        var identityEntry = FindEntry(rootEntries, "implementation_issue_packet");
        var nestedContract = identityEntry?.Value is YamlMappingNode identityForMisplacement
            && FindEntry(identityForMisplacement.Children.ToArray(), "verification_contract") is not null;

        if (contractEntry is null)
        {
            if (nestedContract)
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract belongs at the packet root");
            return NotDeclared(executionUnit, packetPath, sha256);
        }
        var contractPair = contractEntry.Value;
        var identityPair = identityEntry.GetValueOrDefault();

        if (nestedContract)
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract must appear only at the packet root");

        if (HasExplicitTag(root)
            || scan.ExplicitTagPaths.Any(path => path.Length == 0 || IsContractPath(path))
            || HasExplicitTag(contractPair.Value)
            || HasExplicitTag(contractPair.Key)
            || scan.AliasPaths.Any(path => IsContractPath(path)))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract and its participating nodes must not use aliases or explicit tags");

        if (identityEntry is null || identityPair.Value is not YamlMappingNode identity || HasExplicitTag(identityPair.Key) || HasExplicitTag(identity)
            || scan.ExplicitTagPaths.Contains("/implementation_issue_packet"))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-identity", "implementation_issue_packet must be an untagged mapping with source_execution_unit and acceptance_criteria");

        var identityEntries = identity.Children.ToArray();
        var unitEntry = FindEntry(identityEntries, "source_execution_unit");
        var actualCriteriaEntry = FindEntry(identityEntries, "acceptance_criteria");
        if (unitEntry is null || scan.AliasPaths.Any(path => path is "/implementation_issue_packet" or "/implementation_issue_packet/source_execution_unit"))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-identity", "source_execution_unit and acceptance_criteria must be explicit, unaliased packet fields");

        if (actualCriteriaEntry is null)
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "implementation_issue_packet.acceptance_criteria is required");
        if (scan.AliasPaths.Any(path => path is "/implementation_issue_packet/acceptance_criteria" || path.StartsWith("/implementation_issue_packet/acceptance_criteria/", StringComparison.Ordinal)))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "actual acceptance criteria must not use aliases");

        var unitPair = unitEntry.Value;
        var actualCriteriaPair = actualCriteriaEntry.Value;
        if (HasExplicitTag(unitPair.Key) || HasExplicitTag(unitPair.Value)
            || scan.ExplicitTagPaths.Contains("/implementation_issue_packet/source_execution_unit"))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-identity", "source_execution_unit must not use an explicit tag");
        if (HasExplicitTag(actualCriteriaPair.Key) || HasExplicitTag(actualCriteriaPair.Value)
            || scan.ExplicitTagPaths.Any(path => path == "/implementation_issue_packet/acceptance_criteria"
                || path.StartsWith("/implementation_issue_packet/acceptance_criteria/", StringComparison.Ordinal)))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "actual acceptance criteria must not use explicit tags");

        if (!TryText(unitPair.Value, out var declaredUnit) || !RulingArtifact.TryIdentifier(declaredUnit, out _))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-identity", "source_execution_unit must be a canonical textual identifier");
        if (!StringComparer.Ordinal.Equals(declaredUnit, executionUnit))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-identity", "source_execution_unit must exactly match --execution-unit");

        if (actualCriteriaPair.Value is not YamlSequenceNode actualCriteriaNode
            || HasExplicitTag(actualCriteriaNode)
            || actualCriteriaNode.Children.Count is < 1 or > 128)
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "implementation_issue_packet.acceptance_criteria must contain 1 through 128 textual criteria");

        var actualCriteria = new List<string>(actualCriteriaNode.Children.Count);
        foreach (var node in actualCriteriaNode.Children)
        {
            if (HasExplicitTag(node) || !TryText(node, out var text) || !ValidText(text, 16384))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "each actual acceptance criterion must be nonblank text up to 16384 UTF-16 code units");
            actualCriteria.Add(text);
        }

        if (contractPair.Value is not YamlMappingNode contract || HasExplicitTag(contract))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract must be a mapping");
        if (!TryExactMapping(contract, ["version", "acceptance_criteria", "lanes", "affected_lanes", "proofs"], packetPath, out var contractFields, out var failure))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", failure);

        if (!TryText(contractFields["version"], out var version) || version != "1")
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract.version must be the scalar spelling 1");

        if (!TrySequence(contractFields["acceptance_criteria"], 1, 128, out var acNodes))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract.acceptance_criteria must contain 1 through 128 entries");
        var acRegistrations = new List<(string Id, int Index)>();
        var acIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedIndexes = new HashSet<int>();
        foreach (var node in acNodes)
        {
            if (!TryMapping(node, ["id", "criterion_index"], out var fields, out failure))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "acceptance_criteria entry: " + failure);
            if (!TryText(fields["id"], out var id) || !IsIdentifier(id) || !acIds.Add(id))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "acceptance_criteria IDs must be unique canonical identifiers");
            if (!TryCanonicalIndex(fields["criterion_index"], out var index) || index > actualCriteria.Count || !usedIndexes.Add(index))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "criterion_index must be a unique canonical 1-based index of an actual criterion");
            acRegistrations.Add((id, index));
        }
        if (usedIndexes.Count != actualCriteria.Count || Enumerable.Range(1, actualCriteria.Count).Any(index => !usedIndexes.Contains(index)))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "the AC registry must bind every actual acceptance criterion exactly once");

        if (!TrySequence(contractFields["lanes"], 1, 64, out var laneNodes))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract.lanes must contain 1 through 64 entries");
        var laneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var laneDefinitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in laneNodes)
        {
            if (!TryMapping(node, ["id", "weight"], out var fields, out failure))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "lane entry: " + failure);
            if (!TryText(fields["id"], out var id) || !IsIdentifier(id) || !laneIds.Add(id))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "lane IDs must be unique canonical identifiers");
            if (!TryText(fields["weight"], out var weight) || weight is not ("ordinary" or "heavy"))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", $"lane '{id}' weight must be ordinary or heavy");
            laneDefinitions.Add(id, weight);
        }

        if (!TrySequence(contractFields["affected_lanes"], 1, 64, out var affectedNodes))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract.affected_lanes must contain 1 through 64 IDs");
        var affected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in affectedNodes)
        {
            if (!TryText(node, out var id) || !IsIdentifier(id) || !affected.Add(id) || !laneDefinitions.ContainsKey(id))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "affected_lanes must contain distinct exact registered lane IDs");
        }

        if (!TrySequence(contractFields["proofs"], 0, 256, out var proofNodes))
            return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "verification_contract.proofs must contain at most 256 entries");
        var proofIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acIdSet = new HashSet<string>(acRegistrations.Select(registration => registration.Id), StringComparer.Ordinal);
        var proofs = new List<Proof>(proofNodes.Count);
        foreach (var node in proofNodes)
        {
            if (!TryMapping(node, ["id", "acceptance_criteria", "lane", "command", "evidence"], out var fields, out failure))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "proof entry: " + failure);
            if (!TryText(fields["id"], out var id) || !IsIdentifier(id) || !proofIds.Add(id))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", "proof IDs must be unique canonical identifiers");
            if (!TrySequence(fields["acceptance_criteria"], 1, 128, out var references))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", $"proof '{id}' acceptance_criteria must contain 1 through 128 IDs");
            var proofAcs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in references)
            {
                if (!TryText(reference, out var acId) || !IsIdentifier(acId) || !acIdSet.Contains(acId) || !proofAcs.Add(acId))
                    return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", $"proof '{id}' contains an unknown, case-mismatched, or duplicate AC ID");
            }
            if (!TryText(fields["lane"], out var lane) || !laneDefinitions.ContainsKey(lane) || !affected.Contains(lane))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", $"proof '{id}' must reference a registered affected lane exactly");
            if (!TryText(fields["command"], out var command) || !ValidText(command, 16384))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", $"proof '{id}' command must be nonblank text up to 16384 UTF-16 code units");
            if (!TryText(fields["evidence"], out var evidence) || evidence is not ("local-required" or "hosted-required"))
                return InvalidWithBytes(executionUnit, packetPath, sha256, "invalid-schema", $"proof '{id}' evidence must be local-required or hosted-required");
            proofs.Add(new Proof(id, proofAcs.Order(StringComparer.Ordinal).ToArray(), lane, command, evidence));
        }

        var proofIdsByAc = acRegistrations.ToDictionary(item => item.Id, _ => new List<string>(), StringComparer.Ordinal);
        var proofIdsByLane = laneDefinitions.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var proof in proofs)
        {
            foreach (var acId in proof.AcceptanceCriteria) proofIdsByAc[acId].Add(proof.Id);
            proofIdsByLane[proof.Lane].Add(proof.Id);
        }
        var criteriaResults = acRegistrations
            .Select(item => new AcceptanceCriterion(item.Id, item.Index, actualCriteria[item.Index - 1], proofIdsByAc[item.Id].Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var laneResults = laneDefinitions.Select(pair => new Lane(pair.Key, pair.Value, affected.Contains(pair.Key), proofIdsByLane[pair.Key].Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var sortedProofs = proofs.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var missingAcs = criteriaResults.Where(item => item.ProofIds.Count == 0).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        var missingLanes = laneResults.Where(item => item.Affected && item.ProofIds.Count == 0).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        var localLanes = new HashSet<string>(proofs.Where(item => item.Evidence == "local-required").Select(item => item.Lane), StringComparer.Ordinal);
        var missingLocal = laneResults.Where(item => item.Affected && item.Weight == "heavy" && !localLanes.Contains(item.Id))
            .Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();

        var diagnostics = new List<Diagnostic>();
        foreach (var id in missingAcs) diagnostics.Add(new("missing-ac", packetPath, $"acceptance criterion '{id}' has no proof"));
        foreach (var id in missingLanes) diagnostics.Add(new("missing-lane", packetPath, $"affected lane '{id}' has no proof"));
        foreach (var id in missingLocal) diagnostics.Add(new("missing-local-required", packetPath, $"heavy affected lane '{id}' has no local-required proof"));
        var state = diagnostics.Count == 0 ? "complete" : "incomplete";
        var cause = state == "complete" ? "verification-contract-complete" : "verification-contract-incomplete";
        return new Result(executionUnit, state, cause,
            state == "complete"
                ? "Structural coverage only: all registered criteria and affected lanes have structural proof coverage; execution, publication, and authority are not observed or verified."
                : "Structural coverage only: one or more structural coverage requirements are missing; execution, publication, and authority are not observed or verified.",
            packetPath, sha256, "not-observed", criteriaResults, laneResults, sortedProofs,
            missingAcs, missingLanes, missingLocal, diagnostics.OrderBy(item => item.Cause, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Detail, StringComparer.Ordinal).ToArray());
    }

    internal static Result NotDeclared(string executionUnit, string packetPath, string sha256) =>
        Empty(executionUnit, "not-declared", "verification-contract-not-declared", "packet has no root verification_contract", packetPath, sha256, []);

    internal static Result Invalid(string executionUnit, string? packetPath, string? sha256, string diagnosticCause, string detail) =>
        Empty(executionUnit, "invalid", "verification-contract-invalid", detail, packetPath, sha256,
            [new Diagnostic(diagnosticCause, packetPath, detail)]);

    internal static Result Unavailable(string executionUnit, string? packetPath, string? sha256, string diagnosticCause, string detail) =>
        Empty(executionUnit, "unavailable", "verification-contract-unavailable", detail, packetPath, sha256,
            [new Diagnostic(diagnosticCause, packetPath, detail)]);

    private static Result Empty(string executionUnit, string state, string cause, string detail, string? packetPath, string? sha256,
        IReadOnlyList<Diagnostic> diagnostics) =>
        new(executionUnit, state, cause,
            $"Structural coverage only: {detail}; execution, publication, and authority are not observed or verified.",
            packetPath, sha256, "not-observed", [], [], [], [], [], [], diagnostics);

    private sealed record ScanResult(int DocumentCount, bool DepthExceeded, bool DuplicateParticipatingKey,
        HashSet<string> AliasPaths, HashSet<string> ExplicitTagPaths, string? ParseError);

    private sealed class EventFrame(bool mapping, string path)
    {
        public bool IsMapping { get; } = mapping;
        public string Path { get; } = path;
        public HashSet<string> SeenKeys { get; } = new(StringComparer.Ordinal);
        public int NextIndex { get; set; }
        public bool ExpectingKey { get; set; } = mapping;
        public string? CurrentKey { get; set; }
    }

    private static ScanResult ScanYaml(string yaml)
    {
        var stack = new Stack<EventFrame>();
        var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var explicitTags = new HashSet<string>(StringComparer.Ordinal);
        var documents = 0;
        var collectionDepth = 0;
        var tooDeep = false;
        var duplicate = false;
        try
        {
            var parser = new Parser(new StringReader(yaml));
            while (parser.MoveNext())
            {
                switch (parser.Current)
                {
                    case DocumentStart:
                        documents++;
                        break;
                    case MappingStart:
                    {
                        var path = ConsumeNode(stack, null);
                        if (!((MappingStart)parser.Current).IsImplicit && IsExplicitTagPathRelevant(path)) explicitTags.Add(path);
                        collectionDepth++;
                        if (collectionDepth > MaximumDepth) { tooDeep = true; return new(documents, true, duplicate, aliases, explicitTags, null); }
                        stack.Push(new EventFrame(true, path));
                        break;
                    }
                    case SequenceStart:
                    {
                        var path = ConsumeNode(stack, null);
                        if (!((SequenceStart)parser.Current).IsImplicit && IsExplicitTagPathRelevant(path)) explicitTags.Add(path);
                        collectionDepth++;
                        if (collectionDepth > MaximumDepth) { tooDeep = true; return new(documents, true, duplicate, aliases, explicitTags, null); }
                        stack.Push(new EventFrame(false, path));
                        break;
                    }
                    case Scalar scalar:
                    {
                        var anchor = scalar.Anchor.ToString();
                        if (anchor.Length > 0 && scalar.Value is not null) anchors[anchor] = scalar.Value;
                        var keyPosition = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        var key = keyPosition ? scalar.Value : null;
                        var scalarPath = keyPosition && key is not null
                            ? CurrentMapPath(stack) + "/" + EscapePathSegment(key)
                            : CurrentChildPath(stack);
                        // The YAML non-specific `!` tag may retain implicit flags,
                        // so inspect the parser event's resolved tag as well as
                        // its style-specific implicit indicators.
                        var hasExplicitTag = scalar.Tag.ToString() != "?"
                            || (!scalar.IsPlainImplicit && !scalar.IsQuotedImplicit);
                        if (hasExplicitTag && IsExplicitTagPathRelevant(scalarPath))
                            explicitTags.Add(scalarPath);
                        if (keyPosition && key is not null)
                        {
                            var mapPath = stack.Peek().Path;
                            if (IsDuplicateRelevantKeyPath(mapPath, key) && !stack.Peek().SeenKeys.Add(key)) duplicate = true;
                        }
                        ConsumeNode(stack, key);
                        break;
                    }
                    case AnchorAlias alias:
                    {
                        var keyPosition = stack.Count > 0 && stack.Peek().IsMapping && stack.Peek().ExpectingKey;
                        var anchorName = alias.Value.ToString();
                        string? key = null;
                        string path;
                        if (keyPosition && anchors.TryGetValue(anchorName, out var aliasKey))
                        {
                            key = aliasKey;
                            path = CurrentMapPath(stack) + "/" + EscapePathSegment(aliasKey);
                        }
                        else path = CurrentChildPath(stack);
                        if (IsAliasPathRelevant(path)) aliases.Add(path);
                        if (keyPosition && key is not null && IsDuplicateRelevantKeyPath(CurrentMapPath(stack), key) && !stack.Peek().SeenKeys.Add(key)) duplicate = true;
                        ConsumeNode(stack, key);
                        break;
                    }
                    case MappingEnd:
                    case SequenceEnd:
                        if (stack.Count > 0) stack.Pop();
                        collectionDepth--;
                        break;
                }
            }
            return new(documents, tooDeep, duplicate, aliases, explicitTags, null);
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException or ArgumentException)
        {
            return new(documents, tooDeep, duplicate, aliases, explicitTags, exception.Message);
        }
    }

    private static bool IsExplicitTagPathRelevant(string path) =>
        path.Length == 0
        || IsContractPath(path)
        || path == "/implementation_issue_packet"
        || path == "/implementation_issue_packet/source_execution_unit"
        || path == "/implementation_issue_packet/acceptance_criteria"
        || path.StartsWith("/implementation_issue_packet/acceptance_criteria/", StringComparison.Ordinal);

    private static bool IsDuplicateRelevantKeyPath(string path, string key) =>
        (path.Length == 0 && key is "verification_contract" or "implementation_issue_packet")
        || (path == "/implementation_issue_packet" && key is "source_execution_unit" or "acceptance_criteria" or "verification_contract")
        || path == "/verification_contract"
        || path.StartsWith("/verification_contract/", StringComparison.Ordinal)
        || path == "/implementation_issue_packet/verification_contract"
        || path.StartsWith("/implementation_issue_packet/verification_contract/", StringComparison.Ordinal);

    private static bool IsAliasPathRelevant(string path)
    {
        var normalized = path.TrimEnd('/');
        return IsContractPath(normalized)
            || normalized == "/implementation_issue_packet"
            || normalized == "/implementation_issue_packet/source_execution_unit"
            || normalized == "/implementation_issue_packet/acceptance_criteria"
            || normalized.StartsWith("/implementation_issue_packet/acceptance_criteria/", StringComparison.Ordinal);
    }

    private static bool IsContractPath(string path) => path == "/verification_contract" || path.StartsWith("/verification_contract/", StringComparison.Ordinal);

    private static bool IsParticipatingIdentityPath(string path) =>
        path == "/implementation_issue_packet"
        || path == "/implementation_issue_packet/source_execution_unit"
        || path == "/implementation_issue_packet/acceptance_criteria"
        || path.StartsWith("/implementation_issue_packet/acceptance_criteria/", StringComparison.Ordinal);

    private static string CurrentChildPath(Stack<EventFrame> stack)
    {
        if (stack.Count == 0) return string.Empty;
        var frame = stack.Peek();
        if (frame.IsMapping) return frame.ExpectingKey ? frame.Path + "/<key>" : frame.Path + "/" + EscapePathSegment(frame.CurrentKey ?? "<value>");
        return frame.Path + "/" + frame.NextIndex;
    }

    private static string CurrentMapPath(Stack<EventFrame> stack) => stack.Count == 0 ? string.Empty : stack.Peek().Path;

    private static string EscapePathSegment(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string ConsumeNode(Stack<EventFrame> stack, string? scalarKey)
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

    private static KeyValuePair<YamlNode, YamlNode>? FindEntry(IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> entries, string name)
    {
        foreach (var entry in entries)
            if (entry.Key is YamlScalarNode scalar && StringComparer.Ordinal.Equals(scalar.Value, name)) return entry;
        return null;
    }

    private static bool TryExactMapping(YamlNode node, IReadOnlyCollection<string> required, string packetPath,
        out Dictionary<string, YamlNode> fields, out string error)
    {
        fields = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        error = "mapping is required";
        if (node is not YamlMappingNode mapping || HasExplicitTag(mapping)) return false;
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (HasExplicitTag(keyNode) || keyNode is not YamlScalarNode keyScalar || !TryText(keyScalar, out var key))
            {
                error = "mapping keys must be untagged textual scalars";
                return false;
            }
            if (!required.Contains(key, StringComparer.Ordinal))
            {
                error = $"unknown field '{key}'";
                return false;
            }
            if (HasExplicitTag(valueNode))
            {
                error = $"field '{key}' must not use an explicit tag";
                return false;
            }
            fields.Add(key, valueNode);
        }
        foreach (var key in required)
        {
            if (!fields.ContainsKey(key))
            {
                error = $"required field '{key}' is missing";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private static bool TryMapping(YamlNode node, IReadOnlyCollection<string> required, out Dictionary<string, YamlNode> fields, out string error) =>
        TryExactMapping(node, required, string.Empty, out fields, out error);

    private static bool TrySequence(YamlNode node, int minimum, int maximum, out List<YamlNode> values)
    {
        values = [];
        if (node is not YamlSequenceNode sequence || HasExplicitTag(sequence) || sequence.Children.Count < minimum || sequence.Children.Count > maximum)
            return false;
        values.AddRange(sequence.Children);
        return true;
    }

    private static bool TryText(YamlNode node, out string value)
    {
        value = string.Empty;
        if (node is not YamlScalarNode scalar || !TryText(scalar, out value)) return false;
        return true;
    }

    private static bool TryText(YamlScalarNode scalar, out string value)
    {
        value = string.Empty;
        if (HasExplicitTag(scalar) || scalar.Value is null || IsPlainNull(scalar)) return false;
        value = scalar.Value;
        return true;
    }

    private static bool IsPlainNull(YamlScalarNode scalar) =>
        scalar.Style == ScalarStyle.Plain
        && (string.IsNullOrEmpty(scalar.Value) || scalar.Value is "~" or "null" or "Null" or "NULL");

    private static bool HasExplicitTag(YamlNode node) => node.Tag.ToString() is not ("?" or "!");

    private static bool TryCanonicalIndex(YamlNode node, out int index)
    {
        index = 0;
        if (!TryText(node, out var value) || !Regex.IsMatch(value, @"\A[1-9][0-9]*\z", RegexOptions.CultureInvariant)) return false;
        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index);
    }

    private static bool IsIdentifier(string value) => IdentifierPattern.IsMatch(value);
    private static bool ValidText(string value, int maximumUtf16Units) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumUtf16Units;

    private static Result UnavailableWithBytes(string unit, string path, string sha, string diagnosticCause, string detail) =>
        Empty(unit, "unavailable", "verification-contract-unavailable", detail, path, sha, [new(diagnosticCause, path, detail)]);

    private static Result InvalidWithBytes(string unit, string path, string sha, string diagnosticCause, string detail) =>
        Empty(unit, "invalid", "verification-contract-invalid", detail, path, sha, [new(diagnosticCause, path, detail)]);
}
