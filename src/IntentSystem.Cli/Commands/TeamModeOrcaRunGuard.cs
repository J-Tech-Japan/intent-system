using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class TeamModeOrcaRunGuard
{
    public static Action? AfterLockHook { get; set; }
    public static Action? BeforeWriteHook { get; set; }

    public sealed record GuardResult(bool Refused, string? RefusalLine, IReadOnlyList<string> UnreadableTopologyPaths);

    public static GuardResult Evaluate(
        string routingRoot,
        string domain,
        TeamModeState? currentState,
        TeamModeState newState)
    {
        var unreadable = new List<string>();
        var boundTeams = EnumerateBoundTeams(routingRoot, domain, unreadable);
        foreach (var bound in boundTeams)
        {
            var before = TeamModeStore.Resolve(currentState, domain, bound.Team).Mode;
            var after = TeamModeStore.Resolve(newState, domain, bound.Team).Mode;
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                continue;
            }

            var clearCommand = OrcaRunBinding.BuildClearRecordOrcaRunCommand(
                domain,
                bound.Team,
                bound.Role ?? "design",
                bound.RunId ?? "<id>");
            return new GuardResult(
                true,
                $"team-mode-write-refused: orca-run-binding-present: team '{bound.Team}' in domain '{domain}' records an Orca Run binding at {bound.RecordPath}; the set would change its effective mode from '{before}' to '{after}'; clear it first with {clearCommand}",
                unreadable);
        }

        return new GuardResult(false, null, unreadable);
    }

    public static string AppendUnreadableSummarySentence(string summary, IReadOnlyList<string> unreadablePaths)
    {
        if (unreadablePaths.Count == 0)
        {
            return summary;
        }

        var paths = unreadablePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return summary
            + $" Orca Run binding guard: {paths.Length} affected topology record(s) could not be read or parsed and were not checked: {string.Join(", ", paths)}.";
    }

    private static IEnumerable<BoundTeamRecord> EnumerateBoundTeams(
        string routingRoot,
        string domain,
        List<string> unreadablePaths)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var soloRoot = Path.Combine(routingRoot, OrcaRunSoloStore.RelativeRoot, domain);
        if (Directory.Exists(soloRoot))
        {
            foreach (var file in Directory.EnumerateFiles(soloRoot, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                seen.Add(name);
                var read = OrcaRunSoloStore.TryRead(routingRoot, domain, name);
                if (!read.Exists)
                {
                    continue;
                }

                var runId = read.IsUnparseable
                    ? "malformed"
                    : read.RunId is null || !OrcaRunBinding.IsValidRunId(read.RunId)
                        ? "malformed"
                        : read.RunId;
                yield return new BoundTeamRecord(
                    name,
                    OrcaRunSoloStore.RelativePathFor(domain, name),
                    runId,
                    read.Role ?? "design");
            }
        }

        var topologyRoot = Path.Combine(routingRoot, ".intent-cli/topology", domain);
        if (!Directory.Exists(topologyRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(topologyRoot, "*.json"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".", StringComparison.Ordinal) || fileName.EndsWith(".lock", StringComparison.Ordinal))
            {
                continue;
            }

            var team = Path.GetFileNameWithoutExtension(file);
            var relative = NotifyRoleTopologyStore.RelativePathFor(domain, team);
            var bound = TryReadTopologyBinding(file, team, relative, out var unreadableRelative);
            if (unreadableRelative is not null)
            {
                unreadablePaths.Add(unreadableRelative);
                continue;
            }

            if (bound is not null && seen.Add(team))
            {
                yield return bound;
            }
        }
    }

    private static BoundTeamRecord? TryReadTopologyBinding(
        string file,
        string team,
        string relative,
        out string? unreadableRelative)
    {
        unreadableRelative = null;
        try
        {
            var text = GuardedFileRead.ReadAllText(file);
            var document = JsonDocument.Parse(text);
            if (!NotifyRoleTopologyStore.TrySelectTeamPublic(document.RootElement, team, out var teamElement))
            {
                return null;
            }

            if (!teamElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in roles.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("orca_run", out var orcaRun))
                {
                    continue;
                }

                string? runId = null;
                if (orcaRun.ValueKind == JsonValueKind.Object
                    && orcaRun.TryGetProperty("run_id", out var runIdElement)
                    && runIdElement.ValueKind == JsonValueKind.String)
                {
                    runId = runIdElement.GetString();
                }

                if (runId is null || !OrcaRunBinding.IsValidRunId(runId))
                {
                    runId = "malformed";
                }

                return new BoundTeamRecord(team, relative, runId, property.Name);
            }
        }
        catch (Exception)
        {
            unreadableRelative = relative;
        }

        return null;
    }

    private sealed record BoundTeamRecord(string Team, string RecordPath, string? RunId, string? Role);
}
