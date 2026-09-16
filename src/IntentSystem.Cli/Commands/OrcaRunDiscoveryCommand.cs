using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class OrcaRunDiscoveryCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string Usage =
        "Usage: intent-cli session-layer topology orca-runs [--domain <d>] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            return 0;
        }

        string? domainFilter = null;
        var format = FormatMarkdown;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length)
                    {
                        writer.WriteLine("--domain requires a value.");
                        writer.WriteLine(Usage);
                        return 1;
                    }

                    domainFilter = args[++index];
                    break;
                case "--format":
                    if (index + 1 >= args.Length || args[index + 1] is not (FormatJson or FormatMarkdown))
                    {
                        writer.WriteLine("--format must be 'markdown' or 'json'.");
                        writer.WriteLine(Usage);
                        return 1;
                    }

                    format = args[++index];
                    break;
                default:
                    writer.WriteLine($"Unknown argument '{args[index]}'.");
                    writer.WriteLine(Usage);
                    return 1;
            }
        }

        try
        {
            var result = List(context.RepoRoot, domainFilter);
            Emit(writer, format, result);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writer.WriteLine($"orca-runs-unreadable: {exception.Message}");
            return 1;
        }
    }

    internal static OrcaRunsListResult List(string routingRoot, string? domainFilter)
    {
        TeamModeState? teamModeState;
        try
        {
            teamModeState = TeamModeStore.TryRead(routingRoot);
        }
        catch (InvalidOperationException)
        {
            teamModeState = null;
        }

        var bindings = new List<OrcaRunsBindingRow>();
        var unreadable = new List<OrcaRunsUnreadableRecord>();

        if (teamModeState is null && File.Exists(TeamModeStore.ResolvePath(routingRoot)))
        {
            try
            {
                TeamModeStore.TryRead(routingRoot);
            }
            catch (InvalidOperationException exception)
            {
                unreadable.Add(new OrcaRunsUnreadableRecord
                {
                    RecordPath = TeamModeStore.RelativePath,
                    Cause = "team-mode-unreadable",
                    Message = exception.Message,
                });
            }
        }

        EnumerateTopologyBindings(routingRoot, domainFilter, teamModeState, bindings, unreadable);
        EnumerateSoloBindings(routingRoot, domainFilter, teamModeState, bindings, unreadable);

        bindings.Sort(static (left, right) =>
        {
            var domain = string.Compare(left.Domain, right.Domain, StringComparison.Ordinal);
            if (domain != 0) return domain;
            var team = string.Compare(left.Team, right.Team, StringComparison.Ordinal);
            if (team != 0) return team;
            return string.Compare(left.Role, right.Role, StringComparison.Ordinal);
        });

        return new OrcaRunsListResult
        {
            RoutingRoot = routingRoot,
            DomainFilter = domainFilter,
            Bindings = bindings,
            UnreadableRecords = unreadable,
            Summary = "Recorded bindings only; no Run was contacted.",
        };
    }

    private static void EnumerateTopologyBindings(
        string routingRoot,
        string? domainFilter,
        TeamModeState? teamModeState,
        List<OrcaRunsBindingRow> bindings,
        List<OrcaRunsUnreadableRecord> unreadable)
    {
        var topologyRoot = Path.Combine(routingRoot, ".intent-cli/topology");
        if (!Directory.Exists(topologyRoot))
        {
            return;
        }

        foreach (var domainDirectory in Directory.EnumerateDirectories(topologyRoot))
        {
            var domain = Path.GetFileName(domainDirectory);
            if (domainFilter is not null && !string.Equals(domain, domainFilter, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(domainDirectory, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                var team = Path.GetFileNameWithoutExtension(file);
                var relativePath = NotifyRoleTopologyStore.RelativePathFor(domain, team);
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(GuardedFileRead.ReadAllText(file));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    unreadable.Add(new OrcaRunsUnreadableRecord
                    {
                        RecordPath = relativePath,
                        Cause = "topology-file-unparseable",
                        Message = exception.Message,
                    });
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("domain", out var domainElement) && domainElement.ValueKind == JsonValueKind.String
                        && !string.Equals(domainElement.GetString(), domain, StringComparison.Ordinal))
                    {
                        unreadable.Add(new OrcaRunsUnreadableRecord
                        {
                            RecordPath = relativePath,
                            Cause = "topology-identity-mismatch",
                            Message = $"topology domain '{domainElement.GetString()}' does not match path domain '{domain}'.",
                        });
                        continue;
                    }

                    if (!NotifyRoleTopologyStore.TrySelectTeamPublic(root, team, out var teamElement))
                    {
                        unreadable.Add(new OrcaRunsUnreadableRecord
                        {
                            RecordPath = relativePath,
                            Cause = "topology-team-missing",
                            Message = $"topology file does not select team '{team}'.",
                        });
                        continue;
                    }

                    if (!teamElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var shape = OrcaRunTeamShape.Resolve(routingRoot, domain, team, teamModeState);
                    foreach (var property in roles.EnumerateObject())
                    {
                        if (!property.Value.TryGetProperty("orca_run", out _))
                        {
                            continue;
                        }

                        var roleObject = JsonNode.Parse(property.Value.GetRawText()) as JsonObject ?? new JsonObject();
                        var binding = OrcaRunBinding.ParseRoleBindingNode(roleObject["orca_run"]!);
                        var health = OrcaRunBindingHealth.EvaluateDiscoveryTopologyRole(
                            routingRoot,
                            domain,
                            team,
                            property.Name,
                            roleObject,
                            shape);
                        LogicalRoleNormalizer.TryNormalize(property.Name, out var canonical, out _);
                        bindings.Add(new OrcaRunsBindingRow
                        {
                            Domain = domain,
                            Team = team,
                            Role = property.Name,
                            CanonicalRole = canonical,
                            TeamShape = shape.TeamShape,
                            BindingLocation = OrcaRunBinding.TopologyRoleLocation,
                            RecordPath = relativePath,
                            RunId = binding.RunId,
                            ReceivePolicy = binding.ReceivePolicy,
                            Health = health.Health,
                            Findings = health.Findings,
                        });
                    }
                }
            }
        }
    }

    private static void EnumerateSoloBindings(
        string routingRoot,
        string? domainFilter,
        TeamModeState? teamModeState,
        List<OrcaRunsBindingRow> bindings,
        List<OrcaRunsUnreadableRecord> unreadable)
    {
        var soloRoot = Path.Combine(routingRoot, OrcaRunSoloStore.RelativeRoot);
        if (!Directory.Exists(soloRoot))
        {
            return;
        }

        foreach (var domainDirectory in Directory.EnumerateDirectories(soloRoot))
        {
            var domain = Path.GetFileName(domainDirectory);
            if (domainFilter is not null && !string.Equals(domain, domainFilter, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(domainDirectory, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                var team = Path.GetFileNameWithoutExtension(file);
                var relativePath = OrcaRunSoloStore.RelativePathFor(domain, team);
                var read = OrcaRunSoloStore.TryRead(routingRoot, domain, team);
                if (read.IsUnparseable)
                {
                    unreadable.Add(new OrcaRunsUnreadableRecord
                    {
                        RecordPath = relativePath,
                        Cause = "orca-run-file-unparseable",
                        Message = read.UnparseableMessage ?? "unreadable",
                    });
                    continue;
                }

                var shape = OrcaRunTeamShape.Resolve(routingRoot, domain, team, teamModeState);
                var health = OrcaRunBindingHealth.EvaluateDiscoverySoloFile(routingRoot, domain, team, read, shape);
                LogicalRoleNormalizer.TryNormalize(read.Role, out var canonical, out _);
                bindings.Add(new OrcaRunsBindingRow
                {
                    Domain = domain,
                    Team = team,
                    Role = read.Role ?? "design",
                    CanonicalRole = canonical,
                    TeamShape = shape.TeamShape,
                    BindingLocation = OrcaRunBinding.OrcaRunFileLocation,
                    RecordPath = relativePath,
                    RunId = read.RunId,
                    ReceivePolicy = read.ReceivePolicy,
                    Health = health.Health,
                    Findings = health.Findings,
                });
            }
        }
    }

    private static void Emit(TextWriter writer, string format, OrcaRunsListResult result)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine("# Orca Run bindings");
        writer.WriteLine($"routing_root: {result.RoutingRoot}");
        writer.WriteLine($"domain_filter: {result.DomainFilter ?? "<none>"}");
        foreach (var binding in result.Bindings)
        {
            writer.WriteLine($"- {binding.Domain}/{binding.Team}/{binding.Role}: health={binding.Health}; run_id={binding.RunId ?? "null"}");
        }

        foreach (var unreadable in result.UnreadableRecords)
        {
            writer.WriteLine($"- unreadable {unreadable.RecordPath}: {unreadable.Cause}");
        }

        writer.WriteLine(result.Summary);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

internal sealed record OrcaRunsListResult
{
    public required string RoutingRoot { get; init; }
    public string? DomainFilter { get; init; }
    public required IReadOnlyList<OrcaRunsBindingRow> Bindings { get; init; }
    public required IReadOnlyList<OrcaRunsUnreadableRecord> UnreadableRecords { get; init; }
    public required string Summary { get; init; }
}

internal sealed record OrcaRunsBindingRow
{
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string Role { get; init; }
    public string? CanonicalRole { get; init; }
    public string? TeamShape { get; init; }
    public required string BindingLocation { get; init; }
    public required string RecordPath { get; init; }
    public string? RunId { get; init; }
    public string? ReceivePolicy { get; init; }
    public required string Health { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
}

internal sealed record OrcaRunsUnreadableRecord
{
    public required string RecordPath { get; init; }
    public required string Cause { get; init; }
    public required string Message { get; init; }
}
