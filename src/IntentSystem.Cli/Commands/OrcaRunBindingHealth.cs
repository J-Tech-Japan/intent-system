using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record OrcaRunHealthEvaluation
{
    public required string Health { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
}

internal static class OrcaRunBindingHealth
{
    public static OrcaRunHealthEvaluation EvaluateTopologyRole(
        string routingRoot,
        string domain,
        string team,
        string roleKey,
        NotifyRecordedRole record,
        OrcaRunRoleBinding binding,
        OrcaRunTeamShapeResult? shape = null)
    {
        var causes = new List<string>();
        shape ??= OrcaRunTeamShape.Resolve(routingRoot, domain, team);
        EvaluateTopologyRoleCauses(routingRoot, domain, team, roleKey, record, binding, shape, causes);
        return Build(causes);
    }

    public static OrcaRunHealthEvaluation EvaluateSoloFile(
        string routingRoot,
        string domain,
        string team,
        SoloBindingReadResult file,
        OrcaRunTeamShapeResult? shape = null)
    {
        var causes = new List<string>();
        shape ??= OrcaRunTeamShape.Resolve(routingRoot, domain, team);
        EvaluateSoloFileCauses(routingRoot, domain, team, file, shape, causes);
        return Build(causes);
    }

    public static OrcaRunHealthEvaluation EvaluateDiscoveryTopologyRole(
        string routingRoot,
        string domain,
        string team,
        string roleKey,
        JsonObject roleObject,
        OrcaRunTeamShapeResult shape)
    {
        var binding = OrcaRunBinding.ParseRoleBindingNode(roleObject["orca_run"]!);
        var record = BuildRecordFromRoleObject(roleObject);
        var causes = new List<string>();
        EvaluateTopologyRoleCauses(routingRoot, domain, team, roleKey, record, binding, shape, causes);
        return Build(causes);
    }

    public static OrcaRunHealthEvaluation EvaluateDiscoverySoloFile(
        string routingRoot,
        string domain,
        string team,
        SoloBindingReadResult file,
        OrcaRunTeamShapeResult shape)
    {
        var causes = new List<string>();
        EvaluateSoloFileCauses(routingRoot, domain, team, file, shape, causes);
        return Build(causes);
    }

    public static IReadOnlyList<(string Role, string Cause, string Message)> EvaluateTopologyValidateFindings(
        string routingRoot,
        string domain,
        string team,
        NotifyTeamTopology topology,
        bool storeValidationValid,
        bool topologyResolved)
    {
        var findings = new List<(string Role, string Cause, string Message)>();
        var shape = topologyResolved
            ? OrcaRunTeamShape.Resolve(routingRoot, domain, team)
            : null;

        foreach (var (roleKey, record) in topology.Roles.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (record.OrcaRun is null)
            {
                continue;
            }

            var binding = record.OrcaRun;
            var causes = new List<string>();
            AddRow1(binding, causes);
            AddRow3(binding.RunId, causes);
            AddRow4(binding.ReceivePolicy, causes);

            if (topologyResolved && shape is not null)
            {
                EvaluateShapeDependentTopology(routingRoot, domain, team, roleKey, record, binding, shape, topology, causes);
            }

            foreach (var cause in causes)
            {
                findings.Add((roleKey, cause, MessageForTopologyValidate(cause, roleKey, record, binding, shape)));
            }
        }

        if (topologyResolved
            && shape is { Resolved: true, BindingLocation: OrcaRunBinding.TopologyRoleLocation }
            && storeValidationValid
            && !HasBoundRole(topology)
            && (shape.TeamShape is "five-seat" or "four-seat"))
        {
            var requiredKey = topology.Roles.Keys
                .FirstOrDefault(key => LogicalRoleNormalizer.TryNormalize(key, out var canonical, out _)
                    && string.Equals(canonical, shape.RequiredSeat, StringComparison.Ordinal))
                ?? shape.RequiredSeat!;
            findings.Add((
                requiredKey,
                "orca-run-binding-absent",
                $"No Orca Run binding is recorded on the required seat '{requiredKey}'; record one with {OrcaRunBinding.BuildRecordOrcaRunCommand(domain, team, requiredKey)}."));
        }

        return findings;
    }

    public static IReadOnlyList<(string Role, string Cause, string Message)> EvaluateTopologyValidateFindingsFromRawFile(
        string routingRoot,
        string domain,
        string team,
        bool storeValidationValid)
    {
        if (!storeValidationValid)
        {
            return [];
        }

        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(GuardedFileRead.ReadAllText(path));
            if (!NotifyRoleTopologyStore.TrySelectTeamPublic(document.RootElement, team, out var teamElement)
                || !teamElement.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var findings = new List<(string Role, string Cause, string Message)>();
            foreach (var property in roles.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("orca_run", out _))
                {
                    continue;
                }

                var roleObject = JsonNode.Parse(property.Value.GetRawText()) as JsonObject ?? new JsonObject();
                roleObject.TryGetPropertyValue("orca_run", out var orcaRunNode);
                var binding = OrcaRunBinding.ParseRoleBindingNode(orcaRunNode ?? JsonValue.Create((object?)null)!);
                var record = BuildRecordFromRoleObject(roleObject);
                var causes = new List<string>();
                AddRow1(binding, causes);
                AddRow3(binding.RunId, causes);
                AddRow4(binding.ReceivePolicy, causes);
                foreach (var cause in causes)
                {
                    findings.Add((property.Name, cause, MessageForTopologyValidate(cause, property.Name, record, binding, null)));
                }
            }

            return findings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> EvaluateTeamModeValidateFindings(
        string routingRoot,
        string domain,
        string team)
    {
        var path = OrcaRunSoloStore.ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return [];
        }

        var file = OrcaRunSoloStore.TryRead(routingRoot, domain, team);
        if (file.IsUnparseable)
        {
            return [$"orca-run-file-unparseable: {file.UnparseableMessage}"];
        }

        var shape = OrcaRunTeamShape.Resolve(routingRoot, domain, team);
        var evaluation = EvaluateSoloFile(routingRoot, domain, team, file, shape);
        return evaluation.Findings
            .Select(cause => $"{cause}: {MessageForTeamModeValidate(cause, domain, team, file, shape)}")
            .ToArray();
    }

    public static BootstrapOrcaRunBinding? TryResolveBootstrapBinding(string routingRoot, string domain, string team)
    {
        var shape = OrcaRunTeamShape.Resolve(routingRoot, domain, team);
        if (shape.BindingLocation == OrcaRunBinding.TopologyRoleLocation)
        {
            var resolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
            if (!resolution.Resolved || resolution.Topology is null)
            {
                return TryBootstrapFromRawTopology(routingRoot, domain, team);
            }

            foreach (var (roleKey, record) in resolution.Topology.Roles)
            {
                if (record.OrcaRun is null)
                {
                    continue;
                }

                var health = EvaluateTopologyRole(routingRoot, domain, team, roleKey, record, record.OrcaRun, shape);
                return BuildBootstrap(roleKey, OrcaRunBinding.TopologyRoleLocation, health, record.OrcaRun, domain, team);
            }

            return TryBootstrapFromRawTopology(routingRoot, domain, team);
        }

        if (shape.BindingLocation == OrcaRunBinding.OrcaRunFileLocation)
        {
            var file = OrcaRunSoloStore.TryRead(routingRoot, domain, team);
            if (!file.Exists || file.IsUnparseable)
            {
                return null;
            }

            var health = EvaluateSoloFile(routingRoot, domain, team, file, shape);
            var binding = new OrcaRunRoleBinding(file.RunId, file.ReceivePolicy, file.HasOrcaRunObject, null);
            return BuildBootstrap(file.Role ?? "design", OrcaRunBinding.OrcaRunFileLocation, health, binding, domain, team);
        }

        var soloFile = OrcaRunSoloStore.TryRead(routingRoot, domain, team);
        if (soloFile.Exists && !soloFile.IsUnparseable)
        {
            var health = EvaluateSoloFile(routingRoot, domain, team, soloFile, shape);
            var binding = new OrcaRunRoleBinding(soloFile.RunId, soloFile.ReceivePolicy, soloFile.HasOrcaRunObject, null);
            return BuildBootstrap(soloFile.Role ?? "design", OrcaRunBinding.OrcaRunFileLocation, health, binding, domain, team);
        }

        var topologyResolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (topologyResolution.Resolved && topologyResolution.Topology is not null)
        {
            foreach (var (roleKey, record) in topologyResolution.Topology.Roles)
            {
                if (record.OrcaRun is null)
                {
                    continue;
                }

                var health = EvaluateTopologyRole(routingRoot, domain, team, roleKey, record, record.OrcaRun, shape);
                return BuildBootstrap(roleKey, OrcaRunBinding.TopologyRoleLocation, health, record.OrcaRun, domain, team);
            }
        }

        return TryBootstrapFromRawTopology(routingRoot, domain, team);
    }

    private static BootstrapOrcaRunBinding? TryBootstrapFromRawTopology(string routingRoot, string domain, string team)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(GuardedFileRead.ReadAllText(path));
            if (!NotifyRoleTopologyStore.TrySelectTeamPublic(document.RootElement, team, out var teamElement)
                || !teamElement.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var shape = OrcaRunTeamShape.Resolve(routingRoot, domain, team);
            foreach (var property in roles.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("orca_run", out _))
                {
                    continue;
                }

                var roleObject = JsonNode.Parse(property.Value.GetRawText()) as JsonObject ?? new JsonObject();
                var binding = OrcaRunBinding.ParseRoleBindingNode(roleObject["orca_run"]!);
                var record = BuildRecordFromRoleObject(roleObject);
                var health = EvaluateTopologyRole(routingRoot, domain, team, property.Name, record, binding, shape);
                return BuildBootstrap(property.Name, OrcaRunBinding.TopologyRoleLocation, health, binding, domain, team);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        return null;
    }

    private static BootstrapOrcaRunBinding BuildBootstrap(
        string role,
        string location,
        OrcaRunHealthEvaluation health,
        OrcaRunRoleBinding binding,
        string domain,
        string team) => new()
    {
        Role = role,
        BindingLocation = location,
        Health = health.Health,
        RunId = health.Health == OrcaRunBinding.RecordedHealth ? binding.RunId : null,
        ReceivePolicy = health.Health == OrcaRunBinding.RecordedHealth ? binding.ReceivePolicy : null,
        ReceiveInstruction = health.Health == OrcaRunBinding.RecordedHealth
            ? OrcaRunBinding.ReceiveInstructionForPolicy(binding.ReceivePolicy)
            : null,
        Domain = domain,
        Team = team,
    };

    private static void EvaluateTopologyRoleCauses(
        string routingRoot,
        string domain,
        string team,
        string roleKey,
        NotifyRecordedRole record,
        OrcaRunRoleBinding binding,
        OrcaRunTeamShapeResult shape,
        List<string> causes)
    {
        AddRow1(binding, causes);
        AddRow3(binding.RunId, causes);
        AddRow4(binding.ReceivePolicy, causes);
        AddRow5(shape, causes);
        AddRow6(shape, causes);
        AddRow7(shape, causes);
        AddRow9Topology(shape, causes);
        AddRow10(shape, causes);
        AddRow11Topology(shape, causes);
        AddRow12Topology(roleKey, shape, causes);
        AddRow14(record, causes);
        AddRow15(record, causes);
        AddRow16(record, binding.ReceivePolicy, causes);
        AddRow17Topology(routingRoot, domain, team, roleKey, causes);
    }

    private static void EvaluateSoloFileCauses(
        string routingRoot,
        string domain,
        string team,
        SoloBindingReadResult file,
        OrcaRunTeamShapeResult shape,
        List<string> causes)
    {
        if (file.OrcaRunAbsent || file.OrcaRunNull || !file.HasOrcaRunObject || file.RunId is null)
        {
            causes.Add("orca-run-binding-malformed");
        }

        if (!string.Equals(file.Domain, domain, StringComparison.Ordinal)
            || !string.Equals(file.Team, team, StringComparison.Ordinal))
        {
            causes.Add("binding-identity-mismatch");
        }

        if (file.SchemaVersion is null || !string.Equals(file.SchemaVersion, OrcaRunSoloStore.SchemaVersion, StringComparison.Ordinal))
        {
            causes.Add("orca-run-file-schema-unsupported");
        }

        AddRow3(file.RunId, causes);
        AddRow4(file.ReceivePolicy, causes);
        AddRow5(shape, causes);
        AddRow6(shape, causes);
        AddRow7(shape, causes);
        AddRow8(shape, causes);
        AddRow10(shape, causes);
        AddRow11Solo(shape, causes);
        AddRow12Solo(file.Role, shape, causes);
        if (string.IsNullOrWhiteSpace(file.Frontend) && !string.IsNullOrWhiteSpace(file.RunId))
        {
            causes.Add("frontend-missing");
        }

        AddRow15Solo(file, causes);
        AddRow16Solo(file, causes);
        AddRow18(routingRoot, domain, team, causes);
    }

    private static void EvaluateShapeDependentTopology(
        string routingRoot,
        string domain,
        string team,
        string roleKey,
        NotifyRecordedRole record,
        OrcaRunRoleBinding binding,
        OrcaRunTeamShapeResult shape,
        NotifyTeamTopology topology,
        List<string> causes)
    {
        AddRow5(shape, causes);
        AddRow6(shape, causes);
        AddRow7(shape, causes);
        AddRow9Topology(shape, causes);
        AddRow10(shape, causes);
        AddRow11Topology(shape, causes);
        AddRow12Topology(roleKey, shape, causes);
        AddRow14(record, causes);
        AddRow15(record, causes);
        AddRow16(record, binding.ReceivePolicy, causes);
        AddRow17Topology(routingRoot, domain, team, roleKey, causes);
    }

    private static OrcaRunHealthEvaluation Build(List<string> causes)
    {
        var ordered = causes.Distinct(StringComparer.Ordinal).ToList();
        var applicable = OrderedCauses.Where(ordered.Contains).ToList();
        return new OrcaRunHealthEvaluation
        {
            Health = applicable.FirstOrDefault() ?? OrcaRunBinding.RecordedHealth,
            Findings = applicable,
        };
    }

    private static readonly string[] OrderedCauses =
    [
        "orca-run-binding-malformed",
        "binding-identity-mismatch",
        "orca-run-file-schema-unsupported",
        "orca-run-id-malformed",
        "receive-policy-invalid",
        "team-shape-unreadable",
        "team-shape-unrecorded",
        "team-shape-not-bindable",
        "team-shape-solo-entry-not-team-scoped",
        "topology-unresolved",
        "team-shape-unrecognized",
        "binding-location-not-allowed",
        "binding-seat-not-allowed",
        "frontend-missing",
        "receive-policy-herdr-seat",
        "receive-policy-seat-kind-unrecorded",
        "receive-policy-seat-mismatch",
        "binding-duplicate",
        "binding-location-conflict",
    ];

    private static void AddRow1(OrcaRunRoleBinding binding, List<string> causes)
    {
        if (!binding.WellFormed || binding.MalformedCause is not null)
        {
            causes.Add("orca-run-binding-malformed");
        }
    }

    private static void AddRow3(string? runId, List<string> causes)
    {
        if (runId is not null && !OrcaRunBinding.IsValidRunId(runId))
        {
            causes.Add("orca-run-id-malformed");
        }
    }

    private static void AddRow4(string? policy, List<string> causes)
    {
        if (policy is not null && !OrcaRunBinding.IsValidReceivePolicy(policy))
        {
            causes.Add("receive-policy-invalid");
        }
    }

    private static void AddRow5(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (string.Equals(shape.Cause, "team-shape-unreadable", StringComparison.Ordinal))
        {
            causes.Add("team-shape-unreadable");
        }
    }

    private static void AddRow6(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (string.Equals(shape.Cause, "team-shape-unrecorded", StringComparison.Ordinal))
        {
            causes.Add("team-shape-unrecorded");
        }
    }

    private static void AddRow7(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (string.Equals(shape.Cause, "team-shape-not-bindable", StringComparison.Ordinal))
        {
            causes.Add("team-shape-not-bindable");
        }
    }

    private static void AddRow8(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (string.Equals(shape.Cause, "team-shape-solo-entry-not-team-scoped", StringComparison.Ordinal))
        {
            causes.Add("team-shape-solo-entry-not-team-scoped");
        }
    }

    private static void AddRow9Topology(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (string.Equals(shape.Cause, "topology-unresolved", StringComparison.Ordinal))
        {
            causes.Add("topology-unresolved");
        }
    }

    private static void AddRow10(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (string.Equals(shape.Cause, "team-shape-unrecognized", StringComparison.Ordinal))
        {
            causes.Add("team-shape-unrecognized");
        }
    }

    private static void AddRow11Topology(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (shape.Resolved && shape.BindingLocation == OrcaRunBinding.OrcaRunFileLocation)
        {
            causes.Add("binding-location-not-allowed");
        }
    }

    private static void AddRow11Solo(OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (shape.Resolved && shape.BindingLocation == OrcaRunBinding.TopologyRoleLocation)
        {
            causes.Add("binding-location-not-allowed");
        }
    }

    private static void AddRow12Topology(string roleKey, OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (!shape.Resolved || shape.RequiredSeat is null)
        {
            return;
        }

        if (!LogicalRoleNormalizer.TryNormalize(roleKey, out var canonical, out _)
            || !string.Equals(canonical, shape.RequiredSeat, StringComparison.Ordinal))
        {
            causes.Add("binding-seat-not-allowed");
        }
    }

    private static void AddRow12Solo(string? role, OrcaRunTeamShapeResult shape, List<string> causes)
    {
        if (!shape.Resolved)
        {
            return;
        }

        if (!LogicalRoleNormalizer.TryNormalize(role, out var canonical, out _)
            || !string.Equals(canonical, LogicalRoleNormalizer.Architect, StringComparison.Ordinal))
        {
            causes.Add("binding-seat-not-allowed");
        }
    }

    private static void AddRow14(NotifyRecordedRole record, List<string> causes)
    {
        if (string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            causes.Add("receive-policy-herdr-seat");
        }
    }

    private static void AddRow15(NotifyRecordedRole record, List<string> causes)
    {
        if (string.Equals(record.Resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(record.Frontend)
                || record.Frontend is not ("orca" or "claude-app" or "codex-app")))
        {
            causes.Add("receive-policy-seat-kind-unrecorded");
        }
    }

    private static void AddRow15Solo(SoloBindingReadResult file, List<string> causes)
    {
        var frontend = file.Frontend;
        if (string.IsNullOrWhiteSpace(frontend) || frontend is not ("orca" or "claude-app" or "codex-app"))
        {
            causes.Add("receive-policy-seat-kind-unrecorded");
        }
    }

    private static void AddRow16(NotifyRecordedRole record, string? policy, List<string> causes)
    {
        if (policy is null)
        {
            return;
        }

        OrcaRunBinding.EvaluateReceivePolicyForSeat(record, policy, out var cause, out _);
        if (cause == "receive-policy-seat-mismatch")
        {
            causes.Add(cause);
        }
    }

    private static void AddRow16Solo(SoloBindingReadResult file, List<string> causes)
    {
        if (file.ReceivePolicy is null)
        {
            return;
        }

        var record = new NotifyRecordedRole(
            NotifyRecordedRole.ExternalResident,
            null, null, null, null, null, null, file.Frontend);
        OrcaRunBinding.EvaluateReceivePolicyForSeat(record, file.ReceivePolicy, out var cause, out _);
        if (cause == "receive-policy-seat-mismatch")
        {
            causes.Add(cause);
        }
    }

    private static void AddRow17Topology(string routingRoot, string domain, string team, string roleKey, List<string> causes)
    {
        var duplicates = FindTopologyBindingRoleKeys(routingRoot, domain, team);
        if (duplicates.Count > 1 && !string.Equals(duplicates[0], roleKey, StringComparison.Ordinal))
        {
            causes.Add("binding-duplicate");
        }
    }

    private static void AddRow18(string routingRoot, string domain, string team, List<string> causes)
    {
        if (HasTopologyRoleBinding(routingRoot, domain, team))
        {
            causes.Add("binding-location-conflict");
        }
    }

    public static bool HasTopologyRoleBinding(string routingRoot, string domain, string team)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var document = JsonDocument.Parse(GuardedFileRead.ReadAllText(path));
            if (!NotifyRoleTopologyStore.TrySelectTeamPublic(document.RootElement, team, out var teamElement))
            {
                return false;
            }

            if (!teamElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in roles.EnumerateObject())
            {
                if (property.Value.TryGetProperty("orca_run", out _))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    public static IReadOnlyList<string> FindTopologyBindingRoleKeys(string routingRoot, string domain, string team)
    {
        var keys = new List<string>();
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return keys;
        }

        try
        {
            var document = JsonDocument.Parse(GuardedFileRead.ReadAllText(path));
            if (!NotifyRoleTopologyStore.TrySelectTeamPublic(document.RootElement, team, out var teamElement))
            {
                return keys;
            }

            if (!teamElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
            {
                return keys;
            }

            foreach (var property in roles.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (property.Value.TryGetProperty("orca_run", out _))
                {
                    keys.Add(property.Name);
                }
            }
        }
        catch (Exception)
        {
        }

        return keys;
    }

    private static bool HasBoundRole(NotifyTeamTopology topology) =>
        topology.Roles.Values.Any(record => record.OrcaRun is not null);

    private static NotifyRecordedRole BuildRecordFromRoleObject(JsonObject roleObject)
    {
        var resident = roleObject.TryGetPropertyValue("resident", out var residentNode)
            && residentNode is JsonValue residentValue
            && residentValue.TryGetValue<string>(out var residentText)
            ? residentText
            : NotifyRecordedRole.ExternalResident;
        string? frontend = roleObject.TryGetPropertyValue("frontend", out var frontendNode)
            && frontendNode is JsonValue frontendValue
            && frontendValue.TryGetValue<string>(out var frontendText)
            ? frontendText
            : null;
        return new NotifyRecordedRole(resident, null, null, null, null, null, null, frontend);
    }

    public static string MessageForTopologyValidate(
        string cause,
        string roleKey,
        NotifyRecordedRole record,
        OrcaRunRoleBinding binding,
        OrcaRunTeamShapeResult? shape) =>
        cause switch
        {
            "orca-run-binding-malformed" => $"Role '{roleKey}' orca_run is absent, null, not an object, or has a field of the wrong type.",
            "orca-run-id-malformed" => $"Role '{roleKey}' orca_run run_id '{binding.RunId}' does not match ^run_[0-9a-f]{{12}}$.",
            "receive-policy-invalid" => $"Role '{roleKey}' orca_run receive_policy '{binding.ReceivePolicy}' is not orca-push or inbox-pull.",
            "team-shape-unreadable" => shape?.Message ?? "team mode could not be read.",
            "team-shape-unrecorded" => shape?.Message ?? "team mode is unrecorded.",
            "team-shape-not-bindable" => shape?.Message ?? "team mode is not bindable.",
            "team-shape-unrecognized" => shape?.Message ?? "team shape is unrecognized.",
            "binding-location-not-allowed" => $"Role '{roleKey}' records an Orca Run binding that is not allowed for the current team shape.",
            "binding-seat-not-allowed" => $"Role '{roleKey}' is not the required seat for the recorded team shape.",
            "receive-policy-herdr-seat" => $"Role '{roleKey}' is a herdr seat and cannot record a receive policy.",
            "receive-policy-seat-kind-unrecorded" => $"Role '{roleKey}' frontend is not orca, claude-app, or codex-app.",
            "receive-policy-seat-mismatch" => $"Role '{roleKey}' receive_policy does not match the recorded frontend.",
            "binding-duplicate" => $"Another role already records orca_run before '{roleKey}'.",
            _ => cause,
        };

    private static string MessageForTeamModeValidate(
        string cause,
        string domain,
        string team,
        SoloBindingReadResult file,
        OrcaRunTeamShapeResult shape) =>
        cause switch
        {
            "orca-run-binding-malformed" => "solo binding file orca_run is absent, null, not an object, or has a field of the wrong type.",
            "binding-identity-mismatch" => $"solo binding file names domain '{file.Domain}' / team '{file.Team}', not '{domain}' / '{team}'.",
            "orca-run-file-schema-unsupported" => $"solo binding file schema_version is '{file.SchemaVersion ?? "absent"}', not '1'.",
            "orca-run-id-malformed" => $"solo binding run_id '{file.RunId}' does not match ^run_[0-9a-f]{{12}}$.",
            "receive-policy-invalid" => $"solo binding receive_policy '{file.ReceivePolicy}' is not orca-push or inbox-pull.",
            "team-shape-unreadable" => shape.Message ?? "team mode could not be read.",
            "team-shape-unrecorded" => shape.Message ?? "team mode is unrecorded.",
            "team-shape-not-bindable" => shape.Message ?? "team mode is not bindable.",
            "team-shape-solo-entry-not-team-scoped" => shape.Message ?? "solo entry is not team-scoped.",
            "team-shape-unrecognized" => shape.Message ?? "team shape is unrecognized.",
            "binding-location-not-allowed" => "solo binding file is not allowed for the current team shape.",
            "binding-seat-not-allowed" => "solo binding seat is not allowed for the current team shape.",
            "frontend-missing" => "solo binding frontend is missing.",
            "receive-policy-seat-kind-unrecorded" => "solo binding frontend is not orca, claude-app, or codex-app.",
            "receive-policy-seat-mismatch" => "solo binding receive_policy does not match the recorded frontend.",
            "binding-location-conflict" => "topology role binding conflicts with solo binding file.",
            _ => cause,
        };

    public static string BootstrapMarkdownLine(BootstrapOrcaRunBinding binding)
    {
        if (binding.Health == OrcaRunBinding.RecordedHealth)
        {
            return $"Orca Run mailbox (recorded, not verified against Orca): {binding.RunId} on seat '{binding.Role}' ({binding.ReceivePolicy}). {binding.ReceiveInstruction}";
        }

        if (string.Equals(binding.Health, "topology-unresolved", StringComparison.Ordinal))
        {
            return $"Recorded Orca Run binding for seat '{binding.Role}' is not usable (topology-unresolved); the team topology does not resolve, so the shape is undecided. Run intent-cli session-layer topology orca-runs --domain {binding.Domain} --format json for the cause, then repair the topology record.";
        }

        var validateCommand = binding.BindingLocation == OrcaRunBinding.OrcaRunFileLocation
            ? $"intent-cli team-mode validate --domain {binding.Domain} --team {binding.Team} --format json"
            : $"intent-cli session-layer topology validate --domain {binding.Domain} --team {binding.Team} --format json";
        return $"Recorded Orca Run binding for seat '{binding.Role}' is not usable ({binding.Health}); run {validateCommand}.";
    }
}

internal sealed record BootstrapOrcaRunBinding
{
    public required string Role { get; init; }
    public required string BindingLocation { get; init; }
    public required string Health { get; init; }
    public string? RunId { get; init; }
    public string? ReceivePolicy { get; init; }
    public string? ReceiveInstruction { get; init; }
    [JsonIgnore]
    public string Domain { get; init; } = string.Empty;
    [JsonIgnore]
    public string Team { get; init; } = string.Empty;
}
