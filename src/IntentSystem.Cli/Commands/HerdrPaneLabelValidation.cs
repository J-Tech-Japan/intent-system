using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Performs the optional live, read-only check that complements the recorded
/// role-to-pane topology. Herdr owns the display label; intent-cli only
/// reports the operator action that would make the pane recognizable.
/// </summary>
internal static class HerdrPaneLabelValidation
{
    public static SessionLayerTopologyValidation Apply(
        string routingRoot,
        string domain,
        string team,
        SessionLayerTopologyValidation validation)
    {
        if (!validation.Valid)
        {
            return validation;
        }

        var resolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!resolution.Resolved || resolution.Topology is null)
        {
            return AddWarning(validation,
                $"the recorded topology could not be resolved ({resolution.Cause ?? "topology-unavailable"}).");
        }

        NotifyProcessResult panes;
        try
        {
            var runner = NotifyCommand.ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
            panes = runner.Run(
                NotifyCommand.HerdrExecutableFactory?.Invoke() ?? NotifyTransportPaths.ResolveHerdrExecutable(),
                ["pane", "list", "--workspace", resolution.Topology.WorkspaceId]);
        }
        catch (InvalidOperationException exception)
        {
            return AddWarning(validation, exception.Message);
        }

        if (panes.ExitCode != 0)
        {
            return AddWarning(
                validation,
                $"herdr pane list failed: {OneLine(panes.StandardError, panes.StandardOutput)}");
        }

        IReadOnlyList<ObservedHerdrPane> observedPanes;
        try
        {
            observedPanes = ParsePanes(panes.StandardOutput);
        }
        catch (JsonException exception)
        {
            return AddWarning(validation, $"herdr pane list returned invalid JSON: {exception.Message}");
        }

        var findings = validation.Findings.ToList();
        foreach (var (role, record) in resolution.Topology.Roles.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(record.PaneId))
            {
                continue;
            }

            var workspaceId = record.WorkspaceId ?? resolution.Topology.WorkspaceId;
            var pane = observedPanes.FirstOrDefault(candidate =>
                string.Equals(candidate.WorkspaceId, workspaceId, StringComparison.Ordinal)
                && string.Equals(candidate.PaneId, record.PaneId, StringComparison.Ordinal));
            if (pane is null || !string.IsNullOrWhiteSpace(pane.Label))
            {
                continue;
            }

            var command = $"herdr pane rename {record.PaneId} {role}";
            findings.Add(new SessionLayerTopologyFinding(
                role,
                "pane_label",
                "pane-label-missing",
                $"Recorded herdr pane '{record.PaneId}' for logical role '{role}' has no display label. "
                + $"Run `{command}` so the human can identify the pane they are about to supervise. "
                + "intent-cli records the mapping but does not set herdr labels.")
            {
                IsInformational = true,
            });
        }

        return validation with
        {
            Valid = findings.All(finding => finding.IsInformational),
            Findings = findings,
        };
    }

    private static IReadOnlyList<ObservedHerdrPane> ParsePanes(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object)
        {
            root = result;
        }

        if (!root.TryGetProperty("panes", out var panes)
            || panes.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("the response has no array-valued 'result.panes' field");
        }

        var observed = new List<ObservedHerdrPane>();
        foreach (var pane in panes.EnumerateArray())
        {
            if (pane.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var paneId = ReadString(pane, "pane_id");
            if (string.IsNullOrWhiteSpace(paneId))
            {
                continue;
            }

            var workspaceId = ReadString(pane, "workspace_id") ?? WorkspaceFromPane(paneId);
            observed.Add(new ObservedHerdrPane(workspaceId, paneId, ReadString(pane, "label")));
        }

        return observed;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? WorkspaceFromPane(string paneId)
    {
        var separator = paneId.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 ? paneId[..separator] : null;
    }

    private static SessionLayerTopologyValidation AddWarning(
        SessionLayerTopologyValidation validation,
        string message) => validation with
        {
            Warnings = [.. validation.Warnings, $"Live herdr pane-label validation was skipped: {message}"],
        };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record ObservedHerdrPane(string? WorkspaceId, string PaneId, string? Label);
}
